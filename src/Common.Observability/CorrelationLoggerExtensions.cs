using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Common.Observability;

/// <summary>
/// Enriches log output with correlation. Opening a correlation log scope makes the ambient
/// run/correlation ids and the current trace/span ids structured properties on every log written
/// within it.
/// </summary>
public static class CorrelationLoggerExtensions
{
    /// <summary>Structured property carrying the W3C trace id.</summary>
    public const string TraceIdField = "trace_id";

    /// <summary>Structured property carrying the W3C span id.</summary>
    public const string SpanIdField = "span_id";

    /// <summary>
    /// Begins a logging scope carrying the ambient run/correlation ids and current trace/span ids.
    /// Returns null when there is nothing to enrich (no active scope and no current activity).
    /// </summary>
    /// <param name="logger">The logger to scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public static IDisposable? BeginCorrelationScope(this ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var state = new Dictionary<string, object>(StringComparer.Ordinal);

        if (CorrelationScope.Current is { } run)
        {
            state[ObservabilityInstrumentation.RunIdTag] = run.RunId;
            state[ObservabilityInstrumentation.CorrelationIdTag] = run.CorrelationId;
        }

        if (Activity.Current is { } activity)
        {
            state[TraceIdField] = activity.TraceId.ToString();
            state[SpanIdField] = activity.SpanId.ToString();
        }

        return state.Count == 0 ? null : logger.BeginScope(state);
    }
}
