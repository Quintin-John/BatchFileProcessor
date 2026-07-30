using Common.Messaging.Contracts;
using Common.Security.DataProtection;

namespace Common.FileIngestion.Protection;

/// <summary>
/// Applies field-level data protection to a parsed record before it is published: each field is
/// run through the <see cref="IFieldProtector"/> (encrypt or pass through per the classification
/// policy), bound to its (file, record, field) context. Fail-closed — an unclassified field
/// propagates the protector's error rather than leaking.
/// </summary>
public sealed class RecordProtector
{
    private readonly IFieldProtector _protector;

    /// <summary>Creates a record protector.</summary>
    /// <param name="protector">The field protector; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="protector"/> is null.</exception>
    public RecordProtector(IFieldProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    /// <summary>Returns a copy of <paramref name="record"/> with each field protected per policy.</summary>
    /// <param name="fileId">The source file identity (part of the anti-replay binding).</param>
    /// <param name="record">The record to protect; required.</param>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public IngestRecord Protect(string fileId, IngestRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(record);

        var protectedFields = new Dictionary<string, FieldValue>(record.Fields.Count, StringComparer.Ordinal);
        foreach (var pair in record.Fields)
        {
            var context = new FieldProtectionContext(fileId, record.Locator.RecordSeq, pair.Key);
            protectedFields[pair.Key] = _protector.Protect(context, pair.Value);
        }

        return new IngestRecord(record.Locator, protectedFields);
    }
}
