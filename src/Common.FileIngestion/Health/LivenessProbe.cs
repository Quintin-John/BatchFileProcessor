namespace Common.FileIngestion.Health;

/// <summary>
/// Reports liveness from heartbeat staleness: <see cref="HealthStatus.Healthy"/> while beats arrive
/// within the threshold, <see cref="HealthStatus.Unhealthy"/> once they stop (a stalled/deadlocked
/// pipeline) — the signal that warrants a restart, after which the run resumes from its watermark.
/// </summary>
public sealed class LivenessProbe
{
    private readonly Heartbeat _heartbeat;
    private readonly TimeSpan _stalenessThreshold;

    /// <summary>Creates a liveness probe.</summary>
    /// <param name="heartbeat">The heartbeat to observe; required.</param>
    /// <param name="stalenessThreshold">Max time without a beat before unhealthy; must be positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="heartbeat"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stalenessThreshold"/> is not positive.</exception>
    public LivenessProbe(Heartbeat heartbeat, TimeSpan stalenessThreshold)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stalenessThreshold, TimeSpan.Zero);
        _heartbeat = heartbeat;
        _stalenessThreshold = stalenessThreshold;
    }

    /// <summary>Current liveness: healthy while the last beat is within the threshold.</summary>
    public HealthStatus Status =>
        _heartbeat.TimeSinceLastBeat <= _stalenessThreshold ? HealthStatus.Healthy : HealthStatus.Unhealthy;
}
