using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Common.Observability;

/// <summary>
/// A component's telemetry sources: a named <see cref="System.Diagnostics.ActivitySource"/> for
/// spans and a <see cref="System.Diagnostics.Metrics.Meter"/> for metrics. Spans started through
/// <see cref="StartActivity"/> are automatically tagged with the ambient correlation ids.
/// </summary>
public sealed class ObservabilityInstrumentation : IDisposable
{
    /// <summary>Tag key carrying the run id on spans.</summary>
    public const string RunIdTag = "run.id";

    /// <summary>Tag key carrying the correlation id on spans.</summary>
    public const string CorrelationIdTag = "correlation.id";

    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    /// <summary>Creates instrumentation with the given source/meter name.</summary>
    /// <param name="name">Source and meter name (typically the service name); required, non-blank.</param>
    /// <param name="version">Optional version stamped on the source and meter.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    public ObservabilityInstrumentation(string name, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        _activitySource = new ActivitySource(name, version);
        _meter = new Meter(name, version);
    }

    /// <summary>The source and meter name.</summary>
    public string Name { get; }

    /// <summary>The underlying activity source.</summary>
    public ActivitySource ActivitySource => _activitySource;

    /// <summary>The underlying meter.</summary>
    public Meter Meter => _meter;

    /// <summary>
    /// Starts an activity, tagging it with the ambient correlation ids when a scope is active.
    /// Returns null if no listener is sampling this source.
    /// </summary>
    /// <param name="name">Activity (span) name; required, non-blank.</param>
    /// <param name="kind">Activity kind.</param>
    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var activity = _activitySource.StartActivity(name, kind);
        if (activity is not null && CorrelationScope.Current is { } run)
        {
            activity.SetTag(RunIdTag, run.RunId);
            activity.SetTag(CorrelationIdTag, run.CorrelationId);
        }

        return activity;
    }

    /// <summary>Creates a monotonic counter on this meter.</summary>
    /// <param name="name">Counter name; required, non-blank.</param>
    /// <param name="unit">Optional unit.</param>
    /// <param name="description">Optional description.</param>
    public Counter<long> CreateCounter(string name, string? unit = null, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _meter.CreateCounter<long>(name, unit, description);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activitySource.Dispose();
        _meter.Dispose();
        GC.SuppressFinalize(this);
    }
}
