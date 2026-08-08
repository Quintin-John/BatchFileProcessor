using System.Threading.Channels;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Protection;
using Common.FileIngestion.Rejecting;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Decides what becomes of one framed record: skipped, batched for publish, or quarantined.
/// <para>
/// This is the whole of the per-record path, and it is the only thing that needs a parser, a protector or
/// a reject sink — which is why it is a class rather than a method on the pipeline. The pipeline reads and
/// publishes; what an individual record turns into is settled here.
/// </para>
/// <para>
/// A record already covered by the run's resume offset is dropped: a prior run confirmed it, and
/// re-publishing it would rest on consumer de-duplication rather than on the watermark meaning what it says.
/// </para>
/// </summary>
public sealed class RecordStage
{
    private readonly IRecordParser _parser;
    private readonly RecordProtector _protector;
    private readonly RejectSink _rejectSink;
    private readonly IngestionMetrics _metrics;
    private readonly RecordLineage _lineage;

    /// <summary>Creates the stage from its collaborators.</summary>
    /// <param name="parser">Maps a framed record to fields; required.</param>
    /// <param name="protector">Encrypts flagged fields, and a failed record's raw content; required.</param>
    /// <param name="rejectSink">Where quarantined records go; required.</param>
    /// <param name="metrics">Ingestion metrics; required.</param>
    /// <param name="lineage">Per-record lineage; required.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RecordStage(
        IRecordParser parser,
        RecordProtector protector,
        RejectSink rejectSink,
        IngestionMetrics metrics,
        RecordLineage lineage)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(rejectSink);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(lineage);

        _parser = parser;
        _protector = protector;
        _rejectSink = rejectSink;
        _metrics = metrics;
        _lineage = lineage;
    }

    /// <summary>
    /// Processes one framed record, writing a batch to <paramref name="writer"/> whenever one seals.
    /// </summary>
    /// <param name="run">The run the record belongs to; required.</param>
    /// <param name="framed">The framed record.</param>
    /// <param name="writer">Where sealed batches are handed to the publishers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Internal although the type is public: a host assembles this as a pipeline dependency but never drives
    /// it — only the pipeline, which owns the run and the channel, feeds it records.
    /// </remarks>
    internal async ValueTask ProcessAsync(
        FileRun run, FramedRecord framed, ChannelWriter<IngestBatchMessage> writer, CancellationToken cancellationToken)
    {
        // The record's own extent, not a fixed stride: with variable-length framing every record differs.
        _metrics.BytesRead(framed.ByteLength);

        if (framed.ByteOffset < run.ResumeOffset)
        {
            return; // already confirmed by a prior run
        }

        var parseResult = _parser.Parse(framed);
        var locator = new RecordLocator(framed.RecordSeq, framed.ByteOffset, framed.ByteLength, parseResult.RecordType);

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
            await AcceptAsync(run, locator, parseResult, writer, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RejectAsync(run, locator, framed, parseResult, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AcceptAsync(
        FileRun run, RecordLocator locator, RecordParseResult parseResult,
        ChannelWriter<IngestBatchMessage> writer, CancellationToken cancellationToken)
    {
        var protectedRecord = _protector.Protect(run.Provenance.FileId, parseResult.Record!);
        _metrics.RecordParsed(protectedRecord.Locator.RecordType);
        run.Accepted++;
        await _lineage.EmitAsync(run.Provenance, locator, LineageState.Accepted, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var sealedBatch = run.Batcher.Add(protectedRecord);
        if (sealedBatch is null)
        {
            return;
        }

        await run.Window.WaitAsync(cancellationToken).ConfigureAwait(false);   // confirm-window slot (§3.1)
        await writer.WriteAsync(sealedBatch, cancellationToken).ConfigureAwait(false); // backpressure
    }

    private async ValueTask RejectAsync(
        FileRun run, RecordLocator locator, FramedRecord framed, RecordParseResult parseResult,
        CancellationToken cancellationToken)
    {
        // Encrypt the raw record before it reaches the reject queue: a line that failed to parse was never
        // classified, so nothing rules out its carrying sensitive values, and it must not travel in clear.
        var rawRecord = _protector.ProtectRaw(run.Provenance.FileId, framed.RecordSeq, parseResult.RawRecord!);
        await _rejectSink.RejectAsync(run.Provenance, locator, rawRecord, parseResult.Reasons!, cancellationToken)
            .ConfigureAwait(false);
        _metrics.RecordRejected(parseResult.RecordType);
        run.Rejected++;
        await _lineage.EmitAsync(
            run.Provenance, locator, LineageState.Rejected, reasonCode: parseResult.Reasons![0].Code,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
