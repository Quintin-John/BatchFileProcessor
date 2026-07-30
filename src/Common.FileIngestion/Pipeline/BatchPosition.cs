namespace Common.FileIngestion.Pipeline;

/// <summary>
/// The resume position a confirmed batch establishes: its sequence, the byte offset to resume from
/// (one stride past its last record), and its highest record sequence. Maps directly to a watermark.
/// </summary>
public sealed record BatchPosition
{
    /// <summary>0-based batch sequence.</summary>
    public long BatchSeq { get; }

    /// <summary>Byte offset to resume from once this batch (and its whole prefix) is confirmed.</summary>
    public long ByteOffset { get; }

    /// <summary>Highest record sequence in this batch.</summary>
    public long LastRecordSeq { get; }

    /// <summary>Creates a validated position.</summary>
    /// <param name="batchSeq">Batch sequence; non-negative.</param>
    /// <param name="byteOffset">Resume byte offset; non-negative.</param>
    /// <param name="lastRecordSeq">Highest record sequence; non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any value is negative.</exception>
    public BatchPosition(long batchSeq, long byteOffset, long lastRecordSeq)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchSeq);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(lastRecordSeq);

        BatchSeq = batchSeq;
        ByteOffset = byteOffset;
        LastRecordSeq = lastRecordSeq;
    }
}
