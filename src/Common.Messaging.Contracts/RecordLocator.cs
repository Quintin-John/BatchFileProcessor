namespace Common.Messaging.Contracts;

/// <summary>
/// Locates a single record within its source file. Shared by <see cref="IngestRecord"/> and
/// <see cref="RejectMessage"/> so the "where is this record" concept is defined once.
/// </summary>
public sealed record RecordLocator
{
    /// <summary>1-based sequence of the record within its source file.</summary>
    public long RecordSeq { get; }

    /// <summary>Byte offset of the record within its source file.</summary>
    public long ByteOffset { get; }

    /// <summary>Record-type discriminator (e.g. <c>HEAD</c>, <c>TRAN</c>, <c>TRAI</c>).</summary>
    public string RecordType { get; }

    /// <summary>Creates a validated record locator.</summary>
    /// <param name="recordSeq">1-based record sequence; must be at least 1.</param>
    /// <param name="byteOffset">Byte offset in the source file; must be non-negative.</param>
    /// <param name="recordType">Record-type discriminator; required, non-blank.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordSeq"/> is less than 1 or <paramref name="byteOffset"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="recordType"/> is null, empty, or whitespace.</exception>
    public RecordLocator(long recordSeq, long byteOffset, string recordType)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSeq, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);

        RecordSeq = recordSeq;
        ByteOffset = byteOffset;
        RecordType = recordType;
    }
}
