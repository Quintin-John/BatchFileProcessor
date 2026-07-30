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

    /// <summary>Correlation identity for the run that produced this reject (the RunId).</summary>
    public string CorrelationId { get; }

    /// <summary>Content hash / identity of the source file.</summary>
    public string FileId { get; }

    /// <summary>Original source file name.</summary>
    public string FileName { get; }

    /// <summary>Profile that produced this reject.</summary>
    public string Profile { get; }

    /// <summary>Layout version in force when the record was rejected.</summary>
    public string LayoutVersion { get; }

    /// <summary>1-based sequence of the rejected record within its source file.</summary>
    public long RecordSeq { get; }

    /// <summary>Byte offset of the rejected record within its source file.</summary>
    public long ByteOffset { get; }

    /// <summary>Record-type discriminator of the rejected record.</summary>
    public string RecordType { get; }

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
    /// <param name="correlationId">Run correlation id; required, non-blank.</param>
    /// <param name="fileId">Source file identity; required, non-blank.</param>
    /// <param name="fileName">Source file name; required, non-blank.</param>
    /// <param name="profile">Producing profile; required, non-blank.</param>
    /// <param name="layoutVersion">Layout version; required, non-blank.</param>
    /// <param name="recordSeq">1-based record sequence; must be at least 1.</param>
    /// <param name="byteOffset">Byte offset in the source file; must be non-negative.</param>
    /// <param name="recordType">Record-type discriminator; required, non-blank.</param>
    /// <param name="rawRecord">Original record content (clear or encrypted); required.</param>
    /// <param name="reasons">Field-level failures; required, non-empty, no null elements. Copied defensively.</param>
    /// <exception cref="ArgumentException">Any identity is blank, or <paramref name="reasons"/> is empty or contains a null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordSeq"/> is less than 1 or <paramref name="byteOffset"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rawRecord"/> or <paramref name="reasons"/> is null.</exception>
    public RejectMessage(
        string messageId,
        string correlationId,
        string fileId,
        string fileName,
        string profile,
        string layoutVersion,
        long recordSeq,
        long byteOffset,
        string recordType,
        FieldValue rawRecord,
        IReadOnlyList<RejectReason> reasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutVersion);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSeq, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
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
        CorrelationId = correlationId;
        FileId = fileId;
        FileName = fileName;
        Profile = profile;
        LayoutVersion = layoutVersion;
        RecordSeq = recordSeq;
        ByteOffset = byteOffset;
        RecordType = recordType;
        RawRecord = rawRecord;
        Reasons = new ReadOnlyCollection<RejectReason>(copy);
    }
}
