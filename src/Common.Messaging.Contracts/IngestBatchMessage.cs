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

    /// <summary>Correlation identity for the run that produced this message (the RunId).</summary>
    public string CorrelationId { get; }

    /// <summary>Content hash / identity of the source file.</summary>
    public string FileId { get; }

    /// <summary>Original source file name.</summary>
    public string FileName { get; }

    /// <summary>Profile that produced this message (selects layout, destination, etc.).</summary>
    public string Profile { get; }

    /// <summary>Layout version used to map the records, so consumers resolve field types.</summary>
    public string LayoutVersion { get; }

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

    /// <summary>Creates a validated batch message.</summary>
    /// <param name="messageId">Deterministic message id; required, non-blank.</param>
    /// <param name="correlationId">Run correlation id; required, non-blank.</param>
    /// <param name="fileId">Source file identity; required, non-blank.</param>
    /// <param name="fileName">Source file name; required, non-blank.</param>
    /// <param name="profile">Producing profile; required, non-blank.</param>
    /// <param name="layoutVersion">Layout version; required, non-blank.</param>
    /// <param name="batchSeq">0-based batch sequence; must be non-negative.</param>
    /// <param name="records">Batch records; required, non-empty, no null elements. Copied defensively.</param>
    /// <exception cref="ArgumentException">Any identity is blank, or <paramref name="records"/> is empty or contains a null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSeq"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="records"/> is null.</exception>
    public IngestBatchMessage(
        string messageId,
        string correlationId,
        string fileId,
        string fileName,
        string profile,
        string layoutVersion,
        long batchSeq,
        IReadOnlyList<IngestRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSeq);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            throw new ArgumentException("A batch must contain at least one record.", nameof(records));
        }

        var copy = new List<IngestRecord>(records.Count);
        var first = long.MaxValue;
        var last = long.MinValue;
        foreach (var record in records)
        {
            if (record is null)
            {
                throw new ArgumentException("Records must not contain null elements.", nameof(records));
            }

            first = Math.Min(first, record.RecordSeq);
            last = Math.Max(last, record.RecordSeq);
            copy.Add(record);
        }

        MessageId = messageId;
        CorrelationId = correlationId;
        FileId = fileId;
        FileName = fileName;
        Profile = profile;
        LayoutVersion = layoutVersion;
        BatchSeq = batchSeq;
        Records = new ReadOnlyCollection<IngestRecord>(copy);
        FirstRecordSeq = first;
        LastRecordSeq = last;
    }
}
