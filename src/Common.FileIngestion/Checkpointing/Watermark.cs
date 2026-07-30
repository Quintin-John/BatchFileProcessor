namespace Common.FileIngestion.Checkpointing;

/// <summary>
/// The last confirmed position for a file: how far the broker has acknowledged. A restart resumes
/// from here, so it is only ever advanced across the contiguous confirmed prefix (never past a gap).
/// </summary>
public sealed record Watermark
{
    /// <summary>Identity (content hash) of the file this watermark belongs to.</summary>
    public string FileId { get; }

    /// <summary>Byte offset to resume reading from (first byte after the confirmed prefix).</summary>
    public long ByteOffset { get; }

    /// <summary>Highest confirmed record sequence.</summary>
    public long LastRecordSeq { get; }

    /// <summary>Highest confirmed batch sequence.</summary>
    public long BatchSeq { get; }

    /// <summary>Creates a validated watermark.</summary>
    /// <param name="fileId">File identity; required, non-blank.</param>
    /// <param name="byteOffset">Resume byte offset; non-negative.</param>
    /// <param name="lastRecordSeq">Highest confirmed record sequence; non-negative (0 = none).</param>
    /// <param name="batchSeq">Highest confirmed batch sequence; non-negative.</param>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A position value is negative.</exception>
    public Watermark(string fileId, long byteOffset, long lastRecordSeq, long batchSeq)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(lastRecordSeq);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSeq);

        FileId = fileId;
        ByteOffset = byteOffset;
        LastRecordSeq = lastRecordSeq;
        BatchSeq = batchSeq;
    }
}
