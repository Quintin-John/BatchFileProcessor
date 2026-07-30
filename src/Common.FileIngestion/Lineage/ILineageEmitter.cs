namespace Common.FileIngestion.Lineage;

/// <summary>
/// Emits a per-record lineage event. Narrow by design (§1.1) — it only emits; the transport (bounded
/// channel) and export are behind the implementation. Emission applies backpressure rather than
/// dropping (§8): when the buffer is full the returned task does not complete until space frees, so a
/// lineage event is never silently lost.
/// </summary>
public interface ILineageEmitter
{
    /// <summary>Emits a lineage event, awaiting buffer space if the pipeline is saturated.</summary>
    /// <param name="lineageEvent">The event to emit; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lineageEvent"/> is null.</exception>
    ValueTask EmitAsync(LineageEvent lineageEvent, CancellationToken cancellationToken);
}
