using Common.FileIngestion.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using LibHealth = Common.FileIngestion.Health.HealthStatus;

namespace Ingestion.Worker.Health;

/// <summary>
/// Readiness probe endpoint adapter: reports healthy while files are being published cleanly and
/// degraded when publishing is failing (broker/downstream impaired). Degraded does not fail liveness,
/// so the pod is not restarted mid-outage (which would churn the watermark).
/// </summary>
public sealed class ReadinessHealthCheck : IHealthCheck
{
    private readonly ReadinessGate _gate;

    /// <summary>Creates the check.</summary>
    /// <param name="gate">The readiness gate; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gate"/> is null.</exception>
    public ReadinessHealthCheck(ReadinessGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        _gate = gate;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_gate.Status == LibHealth.Healthy
            ? HealthCheckResult.Healthy("Publishing is flowing.")
            : HealthCheckResult.Degraded("Publishing is impaired."));
}
