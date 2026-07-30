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
    /// <summary>AAD field marker binding a whole raw record's ciphertext (it has no single layout field).</summary>
    private const string RawRecordField = "__raw_record__";

    private readonly IFieldProtector _protector;
    private readonly IPayloadProtector _payloadProtector;

    /// <summary>Creates a record protector.</summary>
    /// <param name="protector">The field protector (per-field, policy-driven); required.</param>
    /// <param name="payloadProtector">The payload protector (whole raw records); required.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public RecordProtector(IFieldProtector protector, IPayloadProtector payloadProtector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(payloadProtector);
        _protector = protector;
        _payloadProtector = payloadProtector;
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

    /// <summary>
    /// Encrypts the raw content of a record that failed parsing so it never sits in clear in the reject
    /// queue. The record has no field structure to classify, so it is protected unconditionally.
    /// </summary>
    /// <param name="fileId">Source file identity (part of the anti-replay binding).</param>
    /// <param name="recordSeq">The rejected record's sequence (part of the binding).</param>
    /// <param name="rawRecord">The raw record content; required, non-empty.</param>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is blank, or <paramref name="rawRecord"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rawRecord"/> is null.</exception>
    public EncryptedFieldValue ProtectRaw(string fileId, long recordSeq, string rawRecord)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(rawRecord);

        var context = new FieldProtectionContext(fileId, recordSeq, RawRecordField);
        return _payloadProtector.Protect(context, rawRecord);
    }
}
