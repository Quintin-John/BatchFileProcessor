namespace Common.Observability;

/// <summary>
/// Soft-coded observability configuration. Bound from application config; no values are hardcoded.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>Service name stamped on telemetry (resource, activity source, meter). Required.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Optional service version.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Deployment environment (e.g. <c>prod</c>, <c>staging</c>).</summary>
    public string Environment { get; set; } = "unknown";

    /// <summary>Trace sampling ratio in the range 0..1 (1 = sample everything).</summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>Validates the options. Fail-closed on invalid configuration.</summary>
    /// <exception cref="ArgumentException"><see cref="ServiceName"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SamplingRatio"/> is outside 0..1.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ServiceName);

        if (SamplingRatio is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(SamplingRatio), SamplingRatio, "Sampling ratio must be between 0 and 1.");
        }
    }
}
