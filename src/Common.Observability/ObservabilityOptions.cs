namespace Common.Observability;

/// <summary>
/// Soft-coded observability configuration. Bound from application config; overridable defaults
/// are named constants.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>Default deployment environment when not explicitly configured.</summary>
    public const string DefaultEnvironment = "unknown";

    private const double MinSamplingRatio = 0.0;
    private const double MaxSamplingRatio = 1.0;

    /// <summary>Service name stamped on telemetry (resource, activity source, meter). Required.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Optional service version.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Deployment environment (e.g. <c>prod</c>, <c>staging</c>).</summary>
    public string Environment { get; set; } = DefaultEnvironment;

    /// <summary>Trace sampling ratio in the range 0..1 (1 = sample everything).</summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>Validates the options. Fail-closed on invalid configuration.</summary>
    /// <exception cref="ArgumentException"><see cref="ServiceName"/> or <see cref="Environment"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SamplingRatio"/> is outside 0..1.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Environment);
        ArgumentOutOfRangeException.ThrowIfLessThan(SamplingRatio, MinSamplingRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SamplingRatio, MaxSamplingRatio);
    }
}
