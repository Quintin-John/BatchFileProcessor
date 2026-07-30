using System.Diagnostics.CodeAnalysis;
using Common.FileIngestion.Batching;
using Common.FileIngestion.Checkpointing;
using Common.FileIngestion.Health;
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
    private readonly StreamRecordReader _reader;
    private readonly IRecordParser _parser;
    private readonly RecordProtector _protector;
    private readonly IMessagePublisher _publisher;
    private readonly RejectSink _rejectSink;
    private readonly ICheckpointStore _checkpointStore;
    private readonly IngestionMetrics _metrics;
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
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentNullException.ThrowIfNull(options);

        _reader = reader;
        _parser = parser;
        _protector = protector;
        _publisher = publisher;
        _rejectSink = rejectSink;
        _checkpointStore = checkpointStore;
        _metrics = metrics;
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

        var readPassFileId = await _reader.ReadAsync(
            request.OpenStream(),
            (framed, ct) => ProcessAsync(run, framed, ct),
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(readPassFileId, fileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Source '{request.SourceKey}' changed during processing (hash mismatch).");
        }

        var finalBatch = run.Batcher.Flush();
        if (finalBatch is not null)
        {
            await PublishBatchAsync(run, finalBatch, cancellationToken).ConfigureAwait(false);
        }

        await _checkpointStore.ClearAsync(run.SourceKey, cancellationToken).ConfigureAwait(false);

        return new IngestOutcome(fileId, run.Accepted, run.Rejected, run.Batches);
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

    private async ValueTask ProcessAsync(FileRun run, FramedRecord framed, CancellationToken cancellationToken)
    {
        _metrics.BytesRead(run.Stride);

        if (framed.ByteOffset < run.ResumeOffset)
        {
            return; // already confirmed by a prior run
        }

        var parseResult = _parser.Parse(framed.RecordSeq, framed.ByteOffset, framed.Content);
        if (parseResult.IsSuccess)
        {
            var protectedRecord = _protector.Protect(run.Provenance.FileId, parseResult.Record!);
            _metrics.RecordParsed(protectedRecord.Locator.RecordType);
            run.Accepted++;

            var sealedBatch = run.Batcher.Add(protectedRecord);
            if (sealedBatch is not null)
            {
                await PublishBatchAsync(run, sealedBatch, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var locator = new RecordLocator(framed.RecordSeq, framed.ByteOffset, parseResult.RecordType);
        await _rejectSink.RejectAsync(
            run.Provenance, locator, new ClearFieldValue(parseResult.RawRecord!), parseResult.Reasons!, cancellationToken)
            .ConfigureAwait(false);
        _metrics.RecordRejected(parseResult.RecordType);
        run.Rejected++;
    }

    private async Task PublishBatchAsync(FileRun run, IngestBatchMessage batch, CancellationToken cancellationToken)
    {
        await _publisher.PublishBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        _metrics.BatchPublished();
        _heartbeat.Beat();
        run.Batches++;

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
