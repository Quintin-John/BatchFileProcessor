using System.Collections.ObjectModel;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Parsing;

/// <summary>
/// The outcome of parsing one record: either a mapped <see cref="Record"/>, or a rejection with
/// the record type, the raw record text, and every field-level <see cref="Reasons"/>.
/// </summary>
public sealed class RecordParseResult
{
    private RecordParseResult(
        IngestRecord? record, string recordType, string? rawRecord, IReadOnlyList<RejectReason>? reasons)
    {
        Record = record;
        RecordType = recordType;
        RawRecord = rawRecord;
        Reasons = reasons;
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

    /// <summary>Creates a successful result.</summary>
    /// <param name="record">The mapped record; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public static RecordParseResult Success(IngestRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new RecordParseResult(record, record.Locator.RecordType, null, null);
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

        return new RecordParseResult(null, recordType, rawRecord, new ReadOnlyCollection<RejectReason>(copy));
    }
}
