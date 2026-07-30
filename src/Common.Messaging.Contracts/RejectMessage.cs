using System.Collections.ObjectModel;

namespace Common.Messaging.Contracts;

/// <summary>
/// A record that failed field validation, routed to the reject queue with enough context to
/// diagnose and replay it. Carries the original record content (clear or encrypted, via
/// <see cref="FieldValue"/>) and every field-level failure reason. A carrier, not a value
/// object — identified by (FileId, RecordSeq).
/// </summary>
public sealed class RejectMessage
{
    /// <summary>Deterministic message identity used for dedupe.</summary>
    public string MessageId { get; }

    /// <summary>Source/run provenance for this reject.</summary>
    public MessageProvenance Provenance { get; }

    /// <summary>Where the rejected record sits in its source file.</summary>
    public RecordLocator Locator { get; }

    /// <summary>
    /// The original record content for inspection/repair/replay: a <see cref="ClearFieldValue"/>
    /// (base64 of the raw bytes) for non-sensitive data, or an <see cref="EncryptedFieldValue"/>
    /// when the raw record carries protected data.
    /// </summary>
    public FieldValue RawRecord { get; }

    /// <summary>All field-level failures for this record. Defensively copied; read-only; never empty.</summary>
    public IReadOnlyList<RejectReason> Reasons { get; }

    /// <summary>Creates a validated reject message.</summary>
    /// <param name="messageId">Deterministic message id; required, non-blank.</param>
    /// <param name="provenance">Source/run provenance; required.</param>
    /// <param name="locator">Where the rejected record sits in its source file; required.</param>
    /// <param name="rawRecord">Original record content (clear or encrypted); required.</param>
    /// <param name="reasons">Field-level failures; required, non-empty, no null elements. Copied defensively.</param>
    /// <exception cref="ArgumentException"><paramref name="messageId"/> is blank, or <paramref name="reasons"/> is empty or contains a null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="provenance"/>, <paramref name="locator"/>, <paramref name="rawRecord"/>, or <paramref name="reasons"/> is null.</exception>
    public RejectMessage(
        string messageId,
        MessageProvenance provenance,
        RecordLocator locator,
        FieldValue rawRecord,
        IReadOnlyList<RejectReason> reasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(rawRecord);
        ArgumentNullException.ThrowIfNull(reasons);

        if (reasons.Count == 0)
        {
            throw new ArgumentException("A reject must have at least one reason.", nameof(reasons));
        }

        var copy = new List<RejectReason>(reasons.Count);
        foreach (var reason in reasons)
        {
            if (reason is null)
            {
                throw new ArgumentException("Reasons must not contain null elements.", nameof(reasons));
            }

            copy.Add(reason);
        }

        MessageId = messageId;
        Provenance = provenance;
        Locator = locator;
        RawRecord = rawRecord;
        Reasons = new ReadOnlyCollection<RejectReason>(copy);
    }
}
