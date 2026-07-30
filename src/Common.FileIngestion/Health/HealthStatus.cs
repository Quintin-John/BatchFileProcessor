namespace Common.FileIngestion.Health;

/// <summary>
/// Health severity, mirroring the standard liveness/readiness vocabulary. Liveness reports
/// <see cref="Healthy"/> or <see cref="Unhealthy"/> (deadlock → restart); readiness reports
/// <see cref="Healthy"/> or <see cref="Degraded"/> (downstream open → hold, don't restart).
/// </summary>
public enum HealthStatus
{
    /// <summary>Fully operational.</summary>
    Healthy,

    /// <summary>Operating but downstream is impaired; do not restart.</summary>
    Degraded,

    /// <summary>Not making progress; a restart is warranted.</summary>
    Unhealthy,
}
