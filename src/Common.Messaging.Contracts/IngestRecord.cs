using System.Collections.ObjectModel;

namespace Common.Messaging.Contracts;

/// <summary>
/// One parsed source record: its position in the file plus its mapped field values.
/// This is a carrier, not a value object — records are identified by
/// (FileId, <see cref="RecordSeq"/>) at the message level, so instances are compared
/// by reference rather than by deep structural equality. The field map is defensively
/// copied on construction and exposed read-only.
/// </summary>
public sealed class IngestRecord
{
    /// <summary>1-based sequence of this record within its source file.</summary>
    public long RecordSeq { get; }

    /// <summary>Byte offset of this record within its source file.</summary>
    public long ByteOffset { get; }

    /// <summary>Record-type discriminator (e.g. <c>HEAD</c>, <c>TRAN</c>, <c>TRAI</c>).</summary>
    public string RecordType { get; }

    /// <summary>Mapped field values keyed by layout field name (ordinal). Read-only.</summary>
    public IReadOnlyDictionary<string, FieldValue> Fields { get; }

    /// <summary>Creates a validated ingest record.</summary>
    /// <param name="recordSeq">1-based record sequence; must be at least 1.</param>
    /// <param name="byteOffset">Byte offset in the source file; must be non-negative.</param>
    /// <param name="recordType">Record-type discriminator; required, non-blank.</param>
    /// <param name="fields">Field values by name; required. Copied defensively. Keys must be non-blank and values non-null.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordSeq"/> is less than 1 or <paramref name="byteOffset"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="recordType"/> is blank, or <paramref name="fields"/> contains a blank key or null value.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public IngestRecord(
        long recordSeq,
        long byteOffset,
        string recordType,
        IReadOnlyDictionary<string, FieldValue> fields)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSeq, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentNullException.ThrowIfNull(fields);

        var copy = new Dictionary<string, FieldValue>(fields.Count, StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("Field names must be non-blank.", nameof(fields));
            }

            if (pair.Value is null)
            {
                throw new ArgumentException("Field values must be non-null.", nameof(fields));
            }

            copy[pair.Key] = pair.Value;
        }

        RecordSeq = recordSeq;
        ByteOffset = byteOffset;
        RecordType = recordType;
        Fields = new ReadOnlyDictionary<string, FieldValue>(copy);
    }
}
