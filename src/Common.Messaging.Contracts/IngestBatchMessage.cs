using System.Collections.ObjectModel;

namespace Common.Messaging.Contracts;

/// <summary>
/// One published message carrying a batch of parsed records. The message is the unit of
/// publish/confirm; consumers explode <see cref="Records"/> into per-record state.
/// <see cref="Count"/>, <see cref="FirstRecordSeq"/>, and <see cref="LastRecordSeq"/> are
/// derived from <see cref="Records"/> so they can never disagree with it.
/// </summary>
public sealed class IngestBatchMessage
{
    /// <summary>Deterministic message identity (e.g. <c>{FileId}-{BatchSeq}</c>) used for dedupe.</summary>
    public string MessageId { get; }

    /// <summary>Source/run provenance for this message.</summary>
    public MessageProvenance Provenance { get; }

    /// <summary>0-based sequence of this batch within the file.</summary>
    public long BatchSeq { get; }

    /// <summary>Records in this batch. Defensively copied; read-only; never empty.</summary>
    public IReadOnlyList<IngestRecord> Records { get; }

    /// <summary>Number of records in the batch. Derived from <see cref="Records"/>.</summary>
    public int Count => Records.Count;

    /// <summary>Lowest record sequence in the batch. Derived from <see cref="Records"/>.</summary>
    public long FirstRecordSeq { get; }

    /// <summary>Highest record sequence in the batch. Derived from <see cref="Records"/>.</summary>
    public long LastRecordSeq { get; }

    /// <summary>
    /// Byte offset one past the furthest byte any record in this batch reaches — the resume point once the
    /// batch is confirmed. Derived from <see cref="Records"/> as the max of each record's
    /// <see cref="RecordLocator.EndByteOffset"/> (a max of extents, not the last element), so it never
    /// depends on insertion order and stays correct when records vary in length.
    /// </summary>
    public long EndByteOffset { get; }

    /// <summary>Creates a validated batch message.</summary>
    /// <param name="messageId">Deterministic message id; required, non-blank.</param>
    /// <param name="provenance">Source/run provenance; required.</param>
    /// <param name="batchSeq">0-based batch sequence; must be non-negative.</param>
    /// <param name="records">Batch records; required, non-empty, no null elements. Copied defensively.</param>
    /// <exception cref="ArgumentException"><paramref name="messageId"/> is blank, or <paramref name="records"/> is empty or contains a null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSeq"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="provenance"/> or <paramref name="records"/> is null.</exception>
    public IngestBatchMessage(
        string messageId,
        MessageProvenance provenance,
        long batchSeq,
        IReadOnlyList<IngestRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSeq);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            throw new ArgumentException("A batch must contain at least one record.", nameof(records));
        }

        var copy = new List<IngestRecord>(records.Count);
        var first = long.MaxValue;
        var last = long.MinValue;
        var end = long.MinValue;
        foreach (var record in records)
        {
            if (record is null)
            {
                throw new ArgumentException("Records must not contain null elements.", nameof(records));
            }

            first = Math.Min(first, record.Locator.RecordSeq);
            last = Math.Max(last, record.Locator.RecordSeq);
            end = Math.Max(end, record.Locator.EndByteOffset);
            copy.Add(record);
        }

        MessageId = messageId;
        Provenance = provenance;
        BatchSeq = batchSeq;
        Records = new ReadOnlyCollection<IngestRecord>(copy);
        FirstRecordSeq = first;
        LastRecordSeq = last;
        EndByteOffset = end;
    }
}
