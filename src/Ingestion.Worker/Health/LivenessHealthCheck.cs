using Common.FileIngestion.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using LibHealth = Common.FileIngestion.Health.HealthStatus;

namespace Ingestion.Worker.Health;

/// <summary>
/// Liveness probe endpoint adapter: reports healthy while the ingestion pipeline is making progress
/// (heartbeat within threshold) and unhealthy once beats stop (a stall/deadlock), so the platform
/// restarts the pod — which then resumes from its watermark.
/// </summary>
public sealed class LivenessHealthCheck : IHealthCheck
{
    private readonly LivenessProbe _probe;

    /// <summary>Creates the check.</summary>
    /// <param name="probe">The liveness probe; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="probe"/> is null.</exception>
    public LivenessHealthCheck(LivenessProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_probe.Status == LibHealth.Healthy
            ? HealthCheckResult.Healthy("Pipeline is making progress.")
            : HealthCheckResult.Unhealthy("Pipeline heartbeat is stale; the pipeline appears stalled."));
}
