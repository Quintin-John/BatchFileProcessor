using System.Collections.ObjectModel;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Abstractions;

/// <summary>
/// The outcome of parsing one record: a mapped <see cref="Record"/>, a rejection with the record type, the
/// raw record text and every field-level <see cref="Reasons"/>, or a <see cref="IsSkipped">skip</see> — a
/// control record (header/trailer) the layout marks skip, consumed for framing but neither emitted nor rejected.
/// </summary>
public sealed class RecordParseResult
{
    private RecordParseResult(
        IngestRecord? record, string recordType, string? rawRecord, IReadOnlyList<RejectReason>? reasons, bool skipped)
    {
        Record = record;
        RecordType = recordType;
        RawRecord = rawRecord;
        Reasons = reasons;
        IsSkipped = skipped;
    }

    /// <summary>The mapped record when successful; otherwise null.</summary>
    public IngestRecord? Record { get; }

    /// <summary>The record-type discriminator value (or the offending value when unknown).</summary>
    public string RecordType { get; }

    /// <summary>The raw record text when rejected; otherwise null.</summary>
    public string? RawRecord { get; }

    /// <summary>The field-level failures when rejected; otherwise null.</summary>
    public IReadOnlyList<RejectReason>? Reasons { get; }

    /// <summary>True when the record parsed successfully.</summary>
    public bool IsSuccess => Record is not null;

    /// <summary>True when the record is a skipped control record — consumed for framing, not emitted or rejected.</summary>
    public bool IsSkipped { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="record">The mapped record; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public static RecordParseResult Success(IngestRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new RecordParseResult(record, record.Locator.RecordType, null, null, skipped: false);
    }

    /// <summary>Creates a skipped result for a control record consumed for framing but not emitted.</summary>
    /// <param name="recordType">The record type / discriminator value; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="recordType"/> is null, empty, or whitespace.</exception>
    public static RecordParseResult Skipped(string recordType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        return new RecordParseResult(null, recordType, null, null, skipped: true);
    }

    /// <summary>Creates a rejected result.</summary>
    /// <param name="recordType">The record type or offending discriminator value; required, non-blank.</param>
    /// <param name="rawRecord">The raw record text; required.</param>
    /// <param name="reasons">Field-level failures; required, non-empty, no null elements.</param>
    /// <exception cref="ArgumentException"><paramref name="recordType"/> is blank, or <paramref name="reasons"/> is empty or contains a null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rawRecord"/> or <paramref name="reasons"/> is null.</exception>
    public static RecordParseResult Rejected(string recordType, string rawRecord, IReadOnlyList<RejectReason> reasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentNullException.ThrowIfNull(rawRecord);
        ArgumentNullException.ThrowIfNull(reasons);

        if (reasons.Count == 0)
        {
            throw new ArgumentException("A rejection must have at least one reason.", nameof(reasons));
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

        return new RecordParseResult(null, recordType, rawRecord, new ReadOnlyCollection<RejectReason>(copy), skipped: false);
    }
}
