using Common.FileIngestion.Abstractions;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Common.FileIngestion.Batching;
using Common.FileIngestion.Health;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Ingests one file end to end: validate → resume → stream-parse → protect → batch → confirmed publish →
/// advance watermark, quarantining unparseable records. Ordering guarantees: a first pass frames the whole
/// file into a discarding sink, which both fixes the FileId every message carries and forces every
/// structural fault to surface <em>before</em> a single record is published, so a file the engine rejects
/// never partly ships; the read pass recomputes the FileId as an integrity guard; the watermark is only ever
/// advanced <em>after</em> a batch is broker-confirmed (never ahead of durable delivery); and any
/// publish/checkpoint failure faults the run (fail-closed) leaving the watermark to resume the contiguous
/// confirmed prefix. Not thread-safe per call.
/// </summary>
public sealed class FileIngestionPipeline
{
    private readonly IRecordReader _reader;
    private readonly RecordStage _recordStage;
    private readonly ConfirmedBatchPublisher _batchPublisher;
    private readonly ICheckpointStore _checkpointStore;
    private readonly IngestionTracing _tracing;
    private readonly IngestionOptions _options;

    /// <summary>Creates the pipeline from its collaborators.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public FileIngestionPipeline(
        IRecordReader reader,
        RecordStage recordStage,
        ConfirmedBatchPublisher batchPublisher,
        ICheckpointStore checkpointStore,
        IngestionTracing tracing,
        IngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(recordStage);
        ArgumentNullException.ThrowIfNull(batchPublisher);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        _reader = reader;
        _recordStage = recordStage;
        _batchPublisher = batchPublisher;
        _checkpointStore = checkpointStore;
        _tracing = tracing;
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
                (framed, ct) => _recordStage.ProcessAsync(run, framed, channel.Writer, ct),
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
                await _batchPublisher.PublishAsync(run, batch, pipelineCts.Token).ConfigureAwait(false);
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

    // The identity pass, which is also the structural one. Framing the whole file into a sink that discards
    // every record does two jobs at once: it fixes the FileId, and because it runs exactly the framing the
    // read pass runs, every structural fault the layout can raise — a trailer that fails its declared marker,
    // fewer rows than the header and trailer require, a final record cut short — surfaces here, before a
    // single record has been published. A file the engine is going to reject must never partly ship.
    //
    // The cost is one extra framing pass over a file that was already being read end to end for its hash.
    // Faults that are genuinely unknowable up front — a broker failure part way through, or the file being
    // rewritten between the two passes — can still publish before they surface; that is inherent to
    // publishing incrementally and is what the resume watermark exists to recover from.
    private async Task<string> ComputeFileIdAsync(IngestRequest request, CancellationToken cancellationToken)
    {
        await using var stream = request.OpenStream();
        return await _reader.ReadAsync(stream, DiscardRecord, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask DiscardRecord(FramedRecord framed, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

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
            request.SourceKey, watermark?.ByteOffset ?? 0, provenance, batcher,
            new ConfirmedBatchTracker(firstBatchSeq), _options.PublisherConfirmWindow);
    }
}
