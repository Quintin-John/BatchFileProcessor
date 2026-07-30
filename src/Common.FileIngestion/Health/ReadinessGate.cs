namespace Common.FileIngestion.Health;

/// <summary>
/// Readiness state the pipeline flips as downstream health changes: <see cref="HealthStatus.Healthy"/>
/// when publishing succeeds, <see cref="HealthStatus.Degraded"/> when the publish circuit is open
/// (broker outage). Degraded holds new work without failing liveness — killing the pod mid-outage
/// would churn the watermark and lose in-flight progress. Reads/writes are atomic across threads.
/// </summary>
public sealed class ReadinessGate
{
    private volatile HealthStatus _status = HealthStatus.Healthy;

    /// <summary>Current readiness.</summary>
    public HealthStatus Status => _status;

    /// <summary>Marks downstream healthy (publishing is flowing).</summary>
    public void MarkHealthy() => _status = HealthStatus.Healthy;

    /// <summary>Marks downstream degraded (publish circuit open); does not affect liveness.</summary>
    public void MarkDegraded() => _status = HealthStatus.Degraded;
}
