using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Common.FileIngestion.Batching;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Parsing;
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
        IngestionOptions options)
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
        var run = await BeginRunAsync(request, fileId, cancellationToken).ConfigureAwait(false);

        // Run span covers the whole file; batch spans nest under it and carry traceparent into headers.
        using var fileSpan = _tracing.StartFileActivity(run.Provenance);

        // Decouple read/map from publishing: sealed batches flow through a bounded channel to a publisher
        // task. The bound caps in-flight memory regardless of file size (§3.1) and backpressures the reader.
        var channel = Channel.CreateBounded<IngestBatchMessage>(new BoundedChannelOptions(_options.BatchChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var publisher = ConsumeAndPublishAsync(run, channel.Reader, pipelineCts);

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
            pipelineCts.Cancel();
            var publisherError = await CaptureExceptionAsync(publisher).ConfigureAwait(false);
            if (publisherError is not null and not OperationCanceledException)
            {
                ExceptionDispatchInfo.Throw(publisherError);
            }

            throw;
        }
#pragma warning restore CA1031

        await publisher.ConfigureAwait(false); // propagate a publisher fault on the happy path

        await _checkpointStore.ClearAsync(run.SourceKey, cancellationToken).ConfigureAwait(false);
        return new IngestOutcome(fileId, run.Accepted, run.Rejected, run.Batches);
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
            pipelineCts.Cancel(); // unblock the producer so a full channel can't deadlock the run
            throw;
        }
#pragma warning restore CA1031
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
#pragma warning disable CA1031 // capturing any fault is this helper's purpose
        catch (Exception ex)
        {
            return ex;
        }
#pragma warning restore CA1031
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
        var batcher = new Batcher(
            _options.MaxRecordsPerBatch, _options.MaxContentBytesPerBatch, provenance,
            watermark is null ? 0 : watermark.BatchSeq + 1);

        return new FileRun(request.SourceKey, watermark?.ByteOffset ?? 0, _reader.Stride, provenance, batcher);
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
            await _publisher.PublishBatchAsync(batch, cancellationToken).ConfigureAwait(false);
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
        run.Batches++;
        // With publisher confirms, publish completing IS broker confirmation, so Published collapses into
        // Confirmed — the lineage reflects how the record actually moved.
        await EmitBatchLineageAsync(run, batch, LineageState.Confirmed, reasonCode: null, cancellationToken)
            .ConfigureAwait(false);

        // Resume position = one stride past the highest-offset record in the batch. LastByteOffset is the
        // authoritative max (not Records[^1]), so this does not depend on batch insertion order. For the
        // terminator-less final record this overshoots by the terminator length, which is immaterial: it
        // is always the last batch and the watermark is cleared on completion (a crash in that window
        // resumes past EOF, having already confirmed every record).
        var confirmedOffset = batch.LastByteOffset + run.Stride;
        await _checkpointStore.SaveAsync(
            new Watermark(run.SourceKey, batch.Provenance.FileId, confirmedOffset, batch.LastRecordSeq, batch.BatchSeq),
            cancellationToken)
            .ConfigureAwait(false);
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

    // Per-file run state threaded through the single-threaded read loop: immutable resume/provenance
    // context plus the running tallies. Replaces threading eight parameters through the read callback.
    private sealed class FileRun
    {
        public FileRun(string sourceKey, long resumeOffset, int stride, MessageProvenance provenance, Batcher batcher)
        {
            SourceKey = sourceKey;
            ResumeOffset = resumeOffset;
            Stride = stride;
            Provenance = provenance;
            Batcher = batcher;
        }

        public string SourceKey { get; }
        public long ResumeOffset { get; }
        public int Stride { get; }
        public MessageProvenance Provenance { get; }
        public Batcher Batcher { get; }
        public long Accepted;
        public long Rejected;
        public long Batches;
    }
}
