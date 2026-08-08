using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Health;
using Common.FileIngestion.Lineage;
using Common.FileIngestion.Telemetry;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Publishes one sealed batch and turns the broker's confirmation into a durable resume point.
/// <para>
/// Delivery and the watermark are one responsibility, not two: the watermark exists to record what the
/// broker has confirmed, so nothing may advance it except the code that saw the confirmation. Keeping them
/// together is what makes "never advance ahead of durable delivery" checkable in one place rather than an
/// ordering convention spread across a larger class.
/// </para>
/// <para>
/// Several publishers run this concurrently against the same <see cref="FileRun"/>. Confirms therefore
/// arrive out of order, and the watermark may only advance across the contiguous confirmed prefix — a batch
/// confirmed beyond an unconfirmed gap is held by the run's tracker until the gap fills, so a crash never
/// resumes past an unconfirmed record.
/// </para>
/// </summary>
public sealed class ConfirmedBatchPublisher
{
    private const string PublishFailedReasonCode = "PUBLISH_FAILED";

    private readonly IMessagePublisher _publisher;
    private readonly ICheckpointStore _checkpointStore;
    private readonly IngestionMetrics _metrics;
    private readonly RecordLineage _lineage;
    private readonly IngestionTracing _tracing;
    private readonly Heartbeat _heartbeat;
    private readonly string _batchDestination;

    /// <summary>Creates the publisher from its collaborators.</summary>
    /// <param name="publisher">Transport the batch is published through; required.</param>
    /// <param name="checkpointStore">Where a confirmed position is persisted; required.</param>
    /// <param name="metrics">Ingestion metrics; required.</param>
    /// <param name="lineage">Per-record lineage; required.</param>
    /// <param name="tracing">Ingestion tracing; required.</param>
    /// <param name="heartbeat">Liveness heartbeat; required.</param>
    /// <param name="batchDestination">Queue or topic batches are published to; required, non-blank.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="batchDestination"/> is blank.</exception>
    public ConfirmedBatchPublisher(
        IMessagePublisher publisher,
        ICheckpointStore checkpointStore,
        IngestionMetrics metrics,
        RecordLineage lineage,
        IngestionTracing tracing,
        Heartbeat heartbeat,
        string batchDestination)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchDestination);

        _publisher = publisher;
        _checkpointStore = checkpointStore;
        _metrics = metrics;
        _lineage = lineage;
        _tracing = tracing;
        _heartbeat = heartbeat;
        _batchDestination = batchDestination;
    }

    /// <summary>
    /// Publishes a batch, records its lineage, and advances the run's watermark if the confirmation extends
    /// the contiguous confirmed prefix.
    /// </summary>
    /// <param name="run">The run the batch belongs to; required.</param>
    /// <param name="batch">The sealed batch; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// Internal although the type is public: a host assembles this as a pipeline dependency but never drives
    /// it — only the pipeline, which owns the run, publishes a batch.
    /// </remarks>
    internal async Task PublishAsync(FileRun run, IngestBatchMessage batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(batch);

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

        // Resume position = the end of the furthest record in the batch. EndByteOffset is the authoritative
        // max of the records' own extents (not Records[^1], and not offset + a fixed stride), so it is correct
        // for variable-length framing too. The watermark may only advance across the contiguous confirmed
        // prefix: a batch confirmed beyond an unconfirmed gap is held by the tracker until the gap fills, so
        // a crash never resumes past an unconfirmed record.
        var result = run.Tracker.Confirm(new BatchPosition(batch.BatchSeq, batch.EndByteOffset, batch.LastRecordSeq));
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
        // Skip the whole per-record loop (and the BatchReference) when lineage is off — otherwise this is
        // O(records) of pure no-op emits per batch. The per-record emits elsewhere are already gated inside
        // RecordLineage, so this is the only batch-side lineage-only work that needs the guard.
        if (!_lineage.Enabled)
        {
            return;
        }

        var batchRef = new BatchReference(batch.BatchSeq, batch.MessageId);
        foreach (var record in batch.Records)
        {
            await _lineage.EmitAsync(run.Provenance, record.Locator, state, batchRef, reasonCode, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
