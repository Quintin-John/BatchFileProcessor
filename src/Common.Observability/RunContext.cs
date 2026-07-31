namespace Common.Observability;

/// <summary>
/// Identity for one run: a unique <see cref="RunId"/> and the <see cref="CorrelationId"/> that ties this
/// run's telemetry together. Runs are always started fresh via <see cref="NewRun"/>, so the two are equal —
/// there is no upstream correlation channel for a dropped file to continue.
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

    private static string NewId() => Guid.NewGuid().ToString("N");
}
