namespace Common.FileIngestion.Checkpointing;

/// <summary>
/// The last confirmed position for a source file: how far the broker has acknowledged. A restart
/// resumes from here, so it is only ever advanced across the contiguous confirmed prefix (never past
/// a gap). Keyed by <see cref="SourceKey"/> — a stable identity known before the file is read (the
/// claimed file name) — because the content-hash file id is not known until the read completes and
/// so cannot be the resume key. The watermark is purely positional: the content hash is recomputed
/// by the reader each run for message provenance and is never persisted here. Resume safety relies
/// on the claim/rename in the file source keeping a claimed file immutable while it is processed.
/// </summary>
public sealed record Watermark
{
    /// <summary>Stable resume key (the claimed source file identity), known before reading.</summary>
    public string SourceKey { get; }

    /// <summary>Byte offset to resume reading from (first byte after the confirmed prefix).</summary>
    public long ByteOffset { get; }

    /// <summary>Highest confirmed record sequence.</summary>
    public long LastRecordSeq { get; }

    /// <summary>Highest confirmed batch sequence.</summary>
    public long BatchSeq { get; }

    /// <summary>Creates a validated watermark.</summary>
    /// <param name="sourceKey">Stable resume key; required, non-blank.</param>
    /// <param name="byteOffset">Resume byte offset; non-negative.</param>
    /// <param name="lastRecordSeq">Highest confirmed record sequence; non-negative (0 = none).</param>
    /// <param name="batchSeq">Highest confirmed batch sequence; non-negative.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceKey"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A position value is negative.</exception>
    public Watermark(string sourceKey, long byteOffset, long lastRecordSeq, long batchSeq)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(lastRecordSeq);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSeq);

        SourceKey = sourceKey;
        ByteOffset = byteOffset;
        LastRecordSeq = lastRecordSeq;
        BatchSeq = batchSeq;
    }
}
