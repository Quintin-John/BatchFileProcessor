namespace Common.FileIngestion.Abstractions;

/// <summary>
/// The last confirmed position for a source file: how far the broker has acknowledged. A restart
/// resumes from here, so it is only ever advanced across the contiguous confirmed prefix (never past
/// a gap). Keyed by <see cref="SourceKey"/> — a stable identity known before the file is read (the
/// claimed file name) — because the content-hash <see cref="FileId"/> is not known until the read
/// completes and so cannot be the resume key. <see cref="FileId"/> binds the watermark to the exact
/// content it was recorded against: on resume the pipeline recomputes the file's hash and only
/// resumes when it matches, so a different file that happens to reuse the name (recurring daily
/// batches) can never inherit a stale offset and silently skip records.
/// </summary>
public sealed record Watermark
{
    /// <summary>Stable resume key (the claimed source file identity), known before reading.</summary>
    public string SourceKey { get; }

    /// <summary>Content hash the watermark was recorded against; resume is valid only if it still matches.</summary>
    public string FileId { get; }

    /// <summary>Byte offset to resume reading from (first byte after the confirmed prefix).</summary>
    public long ByteOffset { get; }

    /// <summary>Highest confirmed record sequence.</summary>
    public long LastRecordSeq { get; }

    /// <summary>Highest confirmed batch sequence.</summary>
    public long BatchSeq { get; }

    /// <summary>Creates a validated watermark.</summary>
    /// <param name="sourceKey">Stable resume key; required, non-blank.</param>
    /// <param name="fileId">Content hash the position was confirmed against; required, non-blank.</param>
    /// <param name="byteOffset">Resume byte offset; non-negative.</param>
    /// <param name="lastRecordSeq">Highest confirmed record sequence; non-negative (0 = none).</param>
    /// <param name="batchSeq">Highest confirmed batch sequence; non-negative.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceKey"/> or <paramref name="fileId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A position value is negative.</exception>
    public Watermark(string sourceKey, string fileId, long byteOffset, long lastRecordSeq, long batchSeq)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(lastRecordSeq);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSeq);

        SourceKey = sourceKey;
        FileId = fileId;
        ByteOffset = byteOffset;
        LastRecordSeq = lastRecordSeq;
        BatchSeq = batchSeq;
    }
}
