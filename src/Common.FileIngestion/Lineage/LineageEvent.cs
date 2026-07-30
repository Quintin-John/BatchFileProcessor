using Common.Messaging.Contracts;

namespace Common.FileIngestion.Lineage;

/// <summary>
/// One structured per-record lifecycle event (design §8), stamped with the identity backbone
/// (correlation/file/record, plus the batch reference once known). Emitted at each
/// <see cref="LineageState"/> transition to form the forensic trace of how a record moved. Telemetry,
/// not the system of record — it never carries clear sensitive field data (§8.3): only stable reason codes.
/// </summary>
public sealed record LineageEvent
{
    /// <summary>Run correlation id (the trace id backbone).</summary>
    public string CorrelationId { get; }

    /// <summary>Content-hash identity of the source file.</summary>
    public string FileId { get; }

    /// <summary>Record identity within the file (sequence, byte offset, record type).</summary>
    public RecordLocator Locator { get; }

    /// <summary>The lifecycle transition this event records.</summary>
    public LineageState State { get; }

    /// <summary>When the transition occurred (stamped by the emitter via an injected clock).</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>The batch the record was placed into, once batched; otherwise null.</summary>
    public BatchReference? Batch { get; }

    /// <summary>Stable reason code for a <see cref="LineageState.Rejected"/>/<see cref="LineageState.Failed"/> event; otherwise null. Never a raw field value.</summary>
    public string? ReasonCode { get; }

    /// <summary>Creates a validated lineage event.</summary>
    /// <param name="correlationId">Run correlation id; required, non-blank.</param>
    /// <param name="fileId">File identity; required, non-blank.</param>
    /// <param name="locator">Record identity; required.</param>
    /// <param name="state">Lifecycle state; must be a defined value.</param>
    /// <param name="timestamp">Transition time.</param>
    /// <param name="batch">Batch reference once known; otherwise null.</param>
    /// <param name="reasonCode">Reason code for reject/fail; non-blank if present.</param>
    /// <exception cref="ArgumentException">A required string is blank, <paramref name="reasonCode"/> is present but blank, or <paramref name="state"/> is undefined.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="locator"/> is null.</exception>
    public LineageEvent(
        string correlationId,
        string fileId,
        RecordLocator locator,
        LineageState state,
        DateTimeOffset timestamp,
        BatchReference? batch = null,
        string? reasonCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(locator);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentException($"Undefined lineage state '{state}'.", nameof(state));
        }

        if (reasonCode is not null && string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("Reason code, when present, must be non-blank.", nameof(reasonCode));
        }

        CorrelationId = correlationId;
        FileId = fileId;
        Locator = locator;
        State = state;
        Timestamp = timestamp;
        Batch = batch;
        ReasonCode = reasonCode;
    }
}
