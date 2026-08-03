using System.Collections.ObjectModel;

namespace Common.Messaging.Contracts;

/// <summary>
/// One parsed source record: its <see cref="Locator"/> plus its mapped field values.
/// This is a carrier, not a value object — records are identified by
/// (FileId, RecordSeq) at the message level, so instances are compared by reference
/// rather than by deep structural equality. The field map is defensively copied on
/// construction and exposed read-only.
/// </summary>
public sealed class IngestRecord
{
    /// <summary>Where this record sits in its source file.</summary>
    public RecordLocator Locator { get; }

    /// <summary>Mapped field values keyed by layout field name (ordinal). Read-only.</summary>
    public IReadOnlyDictionary<string, FieldValue> Fields { get; }

    /// <summary>
    /// Optional memo of this record's serialized wire bytes, set once by the producer that first serializes
    /// the record (the batcher, when sizing it for the byte cap) and reused verbatim at publish so the record
    /// is serialized only once. Internal and never itself written to the wire — <see cref="Serialization.IngestRecordJsonConverter"/>
    /// emits it raw when present, or serialises the record normally when absent. The domain data
    /// (<see cref="Locator"/>, <see cref="Fields"/>) is immutable; this is a set-once serialization memo.
    /// </summary>
    internal ReadOnlyMemory<byte>? SerializedForm { get; set; }

    /// <summary>Creates a validated ingest record.</summary>
    /// <param name="locator">Where the record sits in its source file; required.</param>
    /// <param name="fields">Field values by name; required. Copied defensively. Keys must be non-blank and values non-null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locator"/> or <paramref name="fields"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="fields"/> contains a blank key or null value.</exception>
    public IngestRecord(RecordLocator locator, IReadOnlyDictionary<string, FieldValue> fields)
    {
        ArgumentNullException.ThrowIfNull(locator);
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

        Locator = locator;
        Fields = new ReadOnlyDictionary<string, FieldValue>(copy);
    }
}
