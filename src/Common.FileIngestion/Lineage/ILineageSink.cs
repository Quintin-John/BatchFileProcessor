namespace Common.FileIngestion.Lineage;

/// <summary>
/// Exports a lineage event to a backend (structured log / OTLP). The seam that keeps the lineage
/// transport (bounded channel) independent of where events ultimately go, so the export target can
/// change without touching the pipeline or the emitter.
/// </summary>
public interface ILineageSink
{
    /// <summary>Exports a single lineage event.</summary>
    /// <param name="lineageEvent">The event to export; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lineageEvent"/> is null.</exception>
    Task ExportAsync(LineageEvent lineageEvent, CancellationToken cancellationToken);
}
