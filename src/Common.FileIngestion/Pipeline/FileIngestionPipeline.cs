using Common.FileIngestion.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Common.FileIngestion.Batching;
using Common.FileIngestion.Health;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Reading;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Ingests one file end to end: hash → resume → stream-parse → protect → batch → confirmed publish →
/// advance watermark, quarantining unparseable records. Ordering guarantees: the FileId is fixed by a
/// pre-read hash pass so every message carries the same identity; the read pass recomputes it as an
/// integrity guard; the watermark is only ever advanced <em>after</em> a batch is broker-confirmed
/// (never ahead of durable delivery); and any publish/checkpoint failure faults the run (fail-closed)
/// leaving the watermark to resume the contiguous confirmed prefix. Not thread-safe per call.
/// </summary>
public sealed class FileIngestionPipeline
{
    private const string PublishFailedReasonCode = "PUBLISH_FAILED";

    private readonly StreamRecordReader _reader;
    private readonly IRecordParser _parser;
    private readonly RecordProtector _protector;
    private readonly IMessagePublisher _publisher;
    private readonly RejectSink _rejectSink;
    private readonly ICheckpointStore _checkpointStore;
    private readonly IngestionMetrics _metrics;
    private readonly RecordLineage _lineage;
    private readonly IngestionTracing _tracing;
    private readonly Heartbeat _heartbeat;
    private readonly IngestionOptions _options;
    private readonly string _batchDestination;

    /// <summary>Creates the pipeline from its collaborators.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
        Justification = "Orchestrator coordinating distinct single-responsibility collaborators; grouping them " +
                        "into a parameterless data bag would add no invariant (a wrapper smell) and hide the " +
                        "explicit dependencies. Injected once at composition; not a public call surface.")]
    public FileIngestionPipeline(
        StreamRecordReader reader,
        IRecordParser parser,
        RecordProtector protector,
        IMessagePublisher publisher,
        RejectSink rejectSink,
        ICheckpointStore checkpointStore,
        IngestionMetrics metrics,
        RecordLineage lineage,
        IngestionTracing tracing,
        Heartbeat heartbeat,
        IngestionOptions options,
        string batchDestination)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(rejectSink);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchDestination);

        _reader = reader;
        _parser = parser;
        _protector = protector;
        _publisher = publisher;
        _rejectSink = rejectSink;
        _checkpointStore = checkpointStore;
        _metrics = metrics;
        _lineage = lineage;
        _tracing = tracing;
        _heartbeat = heartbeat;
        _options = options;
        _batchDestination = batchDestination;
    }

    /// <summary>Ingests one file, resuming from its watermark if a prior run was interrupted.</summary>
    /// <param name="request">The file to ingest; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidDataException">The file changed between the hash and read passes.</exception>
    public async Task<IngestOutcome> IngestAsync(IngestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fileId = await ComputeFileIdAsync(request, cancellationToken).ConfigureAwait(false);
        using var run = await BeginRunAsync(request, fileId, cancellationToken).ConfigureAwait(false);

        // Run span covers the whole file; batch spans nest under it and carry traceparent into headers.
        using var fileSpan = _tracing.StartFileActivity(run.Provenance);

        // Decouple read/map from publishing: sealed batches flow through a bounded channel to N concurrent
        // publishers. The bound caps in-flight memory regardless of file size (§3.1) and backpressures the
        // reader; fan-out parallelises the network-bound publish (the sole bottleneck, §3). Confirms arrive
        // out of order, so the watermark advances only across the contiguous confirmed prefix (tracker).
        var channel = Channel.CreateBounded<IngestBatchMessage>(new BoundedChannelOptions(_options.BatchChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var publishers = StartPublishers(run, channel.Reader, pipelineCts);

        try
        {
            var readPassFileId = await _reader.ReadAsync(
                request.OpenStream(),
                (framed, ct) => ProcessAsync(run, framed, channel.Writer, ct),
                pipelineCts.Token).ConfigureAwait(false);

            if (!string.Equals(readPassFileId, fileId, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Source '{request.SourceKey}' changed during processing (hash mismatch).");
            }

            var finalBatch = run.Batcher.Flush();
            if (finalBatch is not null)
            {
                await run.Window.WaitAsync(pipelineCts.Token).ConfigureAwait(false); // confirm-window slot
                await channel.Writer.WriteAsync(finalBatch, pipelineCts.Token).ConfigureAwait(false);
            }

            channel.Writer.Complete();
        }
#pragma warning disable CA1031 // coordinate producer/publisher shutdown, then surface the true cause below
        catch (Exception)
        {
            // A publisher fault cancels the producer so it cannot deadlock on a full channel; that surfaces
            // here as cancellation, but the publisher's fault is the real error and must win.
            channel.Writer.TryComplete();
            await pipelineCts.CancelAsync().ConfigureAwait(false);
            var publisherError = await ObservePublishersAsync(publishers).ConfigureAwait(false);
            if (publisherError is not null and not OperationCanceledException)
            {
                ExceptionDispatchInfo.Throw(publisherError);
            }

            throw;
        }
#pragma warning restore CA1031

        var error = await ObservePublishersAsync(publishers).ConfigureAwait(false); // propagate a publisher fault
        if (error is not null)
        {
            ExceptionDispatchInfo.Throw(error);
        }

        await _checkpointStore.ClearAsync(run.SourceKey, cancellationToken).ConfigureAwait(false);
        return new IngestOutcome(fileId, run.Accepted, run.Rejected, run.Batches);
    }

    private Task[] StartPublishers(FileRun run, ChannelReader<IngestBatchMessage> reader, CancellationTokenSource pipelineCts)
    {
        var publishers = new Task[_options.PublisherConcurrency];
        for (var i = 0; i < publishers.Length; i++)
        {
            publishers[i] = ConsumeAndPublishAsync(run, reader, pipelineCts);
        }

        return publishers;
    }

    private async Task ConsumeAndPublishAsync(
        FileRun run, ChannelReader<IngestBatchMessage> reader, CancellationTokenSource pipelineCts)
    {
        try
        {
            await foreach (var batch in reader.ReadAllAsync(pipelineCts.Token).ConfigureAwait(false))
            {
                await PublishBatchAsync(run, batch, pipelineCts.Token).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // any publisher fault must unblock the producer, then propagate
        catch (Exception)
        {
            await pipelineCts.CancelAsync().ConfigureAwait(false); // unblock producer so a full channel can't deadlock
            throw;
        }
#pragma warning restore CA1031
    }

    // Awaits every publisher, returning the most meaningful fault: a real publish/checkpoint error wins
    // over the cancellation it triggers in the other publishers; null if all succeeded.
    private static async Task<Exception?> ObservePublishersAsync(Task[] publishers)
    {
        try
        {
            await Task.WhenAll(publishers).ConfigureAwait(false);
            return null;
        }
#pragma warning disable CA1031 // inspect the individual tasks below to pick the true cause
        catch (Exception)
        {
            // fall through
        }
#pragma warning restore CA1031

        Exception? cancellation = null;
        foreach (var task in publishers)
        {
            if (task.Exception?.InnerExceptions[0] is { } fault)
            {
                if (fault is not OperationCanceledException)
                {
                    return fault;
                }

                cancellation = fault;
            }
            else if (task.IsCanceled)
            {
                cancellation ??= new OperationCanceledException();
            }
        }

        return cancellation;
    }

    private static async Task<string> ComputeFileIdAsync(IngestRequest request, CancellationToken cancellationToken)
    {
        await using var stream = request.OpenStream();
        return await FileIdHasher.ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FileRun> BeginRunAsync(IngestRequest request, string fileId, CancellationToken cancellationToken)
    {
        // Resume only when the stored watermark was recorded against THIS exact content. A file that
        // reuses the name with different content (recurring daily batches) must start from zero, never
        // inherit a stale offset — otherwise its leading records would be silently skipped.
        var loaded = await _checkpointStore.LoadAsync(request.SourceKey, cancellationToken).ConfigureAwait(false);
        var watermark = loaded is not null && string.Equals(loaded.FileId, fileId, StringComparison.Ordinal)
            ? loaded
            : null;

        var provenance = new MessageProvenance(
            request.CorrelationId, fileId, request.FileName, request.ProfileId, request.LayoutVersion);
        var firstBatchSeq = watermark is null ? 0 : watermark.BatchSeq + 1;
        var batcher = new Batcher(_options.MaxRecordsPerBatch, _options.MaxContentBytesPerBatch, provenance, firstBatchSeq);

        return new FileRun(
            request.SourceKey, watermark?.ByteOffset ?? 0, _reader.Stride, provenance, batcher,
            new ConfirmedBatchTracker(firstBatchSeq), _options.PublisherConfirmWindow);
    }

    private async ValueTask ProcessAsync(
        FileRun run, FramedRecord framed, ChannelWriter<IngestBatchMessage> writer, CancellationToken cancellationToken)
    {
        _metrics.BytesRead(run.Stride);

        if (framed.ByteOffset < run.ResumeOffset)
        {
            return; // already confirmed by a prior run
        }

        var parseResult = _parser.Parse(framed.RecordSeq, framed.ByteOffset, framed.Content);
        var locator = new RecordLocator(framed.RecordSeq, framed.ByteOffset, parseResult.RecordType);

        // A skipped control record such as a header or trailer is consumed for framing but never emitted
        // and never rejected, so record it in the lineage for a complete trace and return.
        if (parseResult.IsSkipped)
        {
            await _lineage.EmitAsync(run.Provenance, locator, LineageState.Skipped, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _lineage.EmitAsync(run.Provenance, locator, LineageState.Consumed, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (parseResult.IsSuccess)
        {
            var protectedRecord = _protector.Protect(run.Provenance.FileId, parseResult.Record!);
            _metrics.RecordParsed(protectedRecord.Locator.RecordType);
            run.Accepted++;
            await _lineage.EmitAsync(run.Provenance, locator, LineageState.Accepted, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var sealedBatch = run.Batcher.Add(protectedRecord);
            if (sealedBatch is not null)
            {
                await run.Window.WaitAsync(cancellationToken).ConfigureAwait(false); // confirm-window slot (§3.1)
                await writer.WriteAsync(sealedBatch, cancellationToken).ConfigureAwait(false); // backpressure
            }

            return;
        }

        // Encrypt the raw record before it reaches the reject queue: a failed-parse line can still carry
        // PAN/PII and must never travel in clear.
        var rawRecord = _protector.ProtectRaw(run.Provenance.FileId, framed.RecordSeq, parseResult.RawRecord!);
        await _rejectSink.RejectAsync(run.Provenance, locator, rawRecord, parseResult.Reasons!, cancellationToken)
            .ConfigureAwait(false);
        _metrics.RecordRejected(parseResult.RecordType);
        run.Rejected++;
        await _lineage.EmitAsync(
            run.Provenance, locator, LineageState.Rejected, reasonCode: parseResult.Reasons![0].Code,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishBatchAsync(FileRun run, IngestBatchMessage batch, CancellationToken cancellationToken)
    {
        // Batch span active during publish → the transport injects traceparent into the message headers.
        using var batchSpan = _tracing.StartBatchActivity(batch);

        await EmitBatchLineageAsync(run, batch, LineageState.Batched, reasonCode: null, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _publisher.PublishBatchAsync(batch, _batchDestination, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // record terminal lineage for the batch's records, then rethrow (fail-closed)
        catch (Exception)
        {
            await EmitBatchLineageAsync(run, batch, LineageState.Failed, PublishFailedReasonCode, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
#pragma warning restore CA1031

        _metrics.BatchPublished();
        _heartbeat.Beat();
        Interlocked.Increment(ref run.Batches); // publishers run concurrently
        // With publisher confirms, publish completing IS broker confirmation, so Published collapses into
        // Confirmed — the lineage reflects how the record actually moved.
        await EmitBatchLineageAsync(run, batch, LineageState.Confirmed, reasonCode: null, cancellationToken)
            .ConfigureAwait(false);

        // Resume position = one stride past the highest-offset record in the batch. LastByteOffset is the
        // authoritative max (not Records[^1]). The watermark may only advance across the contiguous confirmed
        // prefix: a batch confirmed beyond an unconfirmed gap is held by the tracker until the gap fills, so
        // a crash never resumes past an unconfirmed record.
        var confirmedOffset = batch.LastByteOffset + run.Stride;
        var result = run.Tracker.Confirm(new BatchPosition(batch.BatchSeq, confirmedOffset, batch.LastRecordSeq));
        if (result.AdvancedTo is not null)
        {
            await SaveWatermarkAsync(run, result.AdvancedTo, cancellationToken).ConfigureAwait(false);
        }

        if (result.AdvancedCount > 0)
        {
            run.Window.Release(result.AdvancedCount); // free the confirm-window slots that became contiguous
        }
    }

    private async Task SaveWatermarkAsync(FileRun run, BatchPosition position, CancellationToken cancellationToken)
    {
        // Serialise watermark writes across publishers and enforce monotonic advance: concurrent confirms
        // can present advances out of order, and the watermark must never move backward.
        await run.WatermarkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (position.BatchSeq <= run.LastSavedBatchSeq)
            {
                return; // a newer watermark was already persisted
            }

            run.LastSavedBatchSeq = position.BatchSeq;
            await _checkpointStore.SaveAsync(
                new Watermark(
                    run.SourceKey, run.Provenance.FileId, position.ByteOffset, position.LastRecordSeq, position.BatchSeq),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            run.WatermarkGate.Release();
        }
    }

    private async ValueTask EmitBatchLineageAsync(
        FileRun run, IngestBatchMessage batch, LineageState state, string? reasonCode, CancellationToken cancellationToken)
    {
        var batchRef = new BatchReference(batch.BatchSeq, batch.MessageId);
        foreach (var record in batch.Records)
        {
            await _lineage.EmitAsync(run.Provenance, record.Locator, state, batchRef, reasonCode, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Per-file run state. Immutable resume/provenance context plus running tallies. The reader mutates the
    // batcher and Accepted/Rejected (single producer thread); concurrent publishers mutate Batches (via
    // Interlocked) and advance the watermark through the tracker under WatermarkGate.
    private sealed class FileRun : IDisposable
    {
        public FileRun(
            string sourceKey, long resumeOffset, int stride, MessageProvenance provenance, Batcher batcher,
            ConfirmedBatchTracker tracker, int confirmWindow)
        {
            SourceKey = sourceKey;
            ResumeOffset = resumeOffset;
            Stride = stride;
            Provenance = provenance;
            Batcher = batcher;
            Tracker = tracker;
            Window = new SemaphoreSlim(confirmWindow, confirmWindow);
        }

        public string SourceKey { get; }
        public long ResumeOffset { get; }
        public int Stride { get; }
        public MessageProvenance Provenance { get; }
        public Batcher Batcher { get; }
        public ConfirmedBatchTracker Tracker { get; }

        // Serialises watermark writes across publishers and enforces monotonic advance.
        public SemaphoreSlim WatermarkGate { get; } = new(1, 1);
        public long LastSavedBatchSeq { get; set; } = -1;

        // Outstanding-confirms window: a slot per created batch, released when it joins the contiguous
        // confirmed prefix — bounding batches-in-flight (and the tracker's held set) to the window size.
        public SemaphoreSlim Window { get; }

        public long Accepted;
        public long Rejected;
        public long Batches;

        public void Dispose()
        {
            WatermarkGate.Dispose();
            Window.Dispose();
        }
    }
}
