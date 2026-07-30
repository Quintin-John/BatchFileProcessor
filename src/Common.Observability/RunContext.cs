namespace Common.Observability;

/// <summary>
/// Identity for one run: a unique <see cref="RunId"/> and the <see cref="CorrelationId"/> that
/// ties this run's telemetry to any upstream work. For a fresh run the two are equal; a run
/// continued from an upstream trace keeps the upstream correlation id.
/// </summary>
public sealed record RunContext
{
    /// <summary>Unique identity of this run.</summary>
    public string RunId { get; }

    /// <summary>Correlation id propagated across logs, spans, and messages.</summary>
    public string CorrelationId { get; }

    /// <summary>Creates a run context.</summary>
    /// <param name="runId">Unique run id; required, non-blank.</param>
    /// <param name="correlationId">Correlation id; required, non-blank.</param>
    /// <exception cref="ArgumentException">Either argument is null, empty, or whitespace.</exception>
    public RunContext(string runId, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        RunId = runId;
        CorrelationId = correlationId;
    }

    /// <summary>Starts a fresh run whose correlation id equals its run id.</summary>
    public static RunContext NewRun()
    {
        var id = NewId();
        return new RunContext(id, id);
    }

    /// <summary>Starts a new run that continues an upstream correlation id.</summary>
    /// <param name="correlationId">The upstream correlation id to keep; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="correlationId"/> is null, empty, or whitespace.</exception>
    public static RunContext ContinuedFrom(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new RunContext(NewId(), correlationId);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
