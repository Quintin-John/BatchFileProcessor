using Common.Messaging.Contracts;

namespace Common.FileIngestion.Lineage;

/// <summary>
/// Builds and emits per-record <see cref="LineageEvent"/>s for the pipeline: assembles the identity
/// backbone from the run's provenance, stamps the transition time from an injected clock, and emits
/// through the <see cref="ILineageEmitter"/>. Single-sources the provenance→event mapping so every
/// emit site is identical, and keeps the clock injectable for deterministic tests.
/// </summary>
public sealed class RecordLineage
{
    private readonly ILineageEmitter _emitter;
    private readonly TimeProvider _clock;
    private readonly bool _enabled;

    /// <summary>Creates the lineage helper.</summary>
    /// <param name="emitter">The lineage emitter; required.</param>
    /// <param name="clock">Clock for transition timestamps; required.</param>
    /// <param name="enabled">
    /// Whether lineage is produced. When false, <see cref="EmitAsync"/> is a no-op that allocates nothing —
    /// a deliberate operator opt-out, uniform across the run (all transitions or none), never a partial trace.
    /// When true, behaviour is unchanged: every transition is emitted with the block-never-drop guarantee (§8).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="emitter"/> or <paramref name="clock"/> is null.</exception>
    public RecordLineage(ILineageEmitter emitter, TimeProvider clock, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentNullException.ThrowIfNull(clock);
        _emitter = emitter;
        _clock = clock;
        _enabled = enabled;
    }

    /// <summary>Emits a lineage event for one record's transition.</summary>
    /// <param name="provenance">Run provenance (correlation + file identity); required.</param>
    /// <param name="locator">Record identity; required.</param>
    /// <param name="state">The transition.</param>
    /// <param name="batch">Batch reference, once known; otherwise null.</param>
    /// <param name="reasonCode">Reason code for reject/fail (never a raw value); otherwise null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provenance"/> or <paramref name="locator"/> is null.</exception>
    public ValueTask EmitAsync(
        MessageProvenance provenance,
        RecordLocator locator,
        LineageState state,
        BatchReference? batch = null,
        string? reasonCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        // Gate before building the event: when lineage is disabled, no LineageEvent is allocated and the
        // channel is never touched — the per-record emission cost is fully removed.
        if (!_enabled)
        {
            return ValueTask.CompletedTask;
        }

        var lineageEvent = new LineageEvent(
            provenance.CorrelationId, provenance.FileId, locator, state, _clock.GetUtcNow(), batch, reasonCode);
        return _emitter.EmitAsync(lineageEvent, cancellationToken);
    }
}
