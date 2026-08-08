using System.Text.Json.Serialization;

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

    /// <summary>
    /// Total bytes this record occupies in its source file, including any record terminator. A record's
    /// extent is part of locating it: <see cref="EndByteOffset"/> is where the next record begins, which is
    /// what a resume point must be set to. Variable for delimited framing, constant for fixed-width.
    /// </summary>
    public int ByteLength { get; }

    /// <summary>Record-type discriminator (e.g. <c>HEAD</c>, <c>TRAN</c>, <c>TRAI</c>).</summary>
    public string RecordType { get; }

    /// <summary>
    /// Byte offset one past this record's last byte — the offset of the next record. Derived from
    /// <see cref="ByteOffset"/> and <see cref="ByteLength"/> so the two can never disagree. Kept off the
    /// wire: publishing a derived value invites it to contradict the two authoritative ones.
    /// </summary>
    [JsonIgnore]
    public long EndByteOffset => ByteOffset + ByteLength;

    /// <summary>Creates a validated record locator.</summary>
    /// <param name="recordSeq">1-based record sequence; must be at least 1.</param>
    /// <param name="byteOffset">Byte offset in the source file; must be non-negative.</param>
    /// <param name="byteLength">Bytes occupied in the source file including any terminator; must be at least 1.</param>
    /// <param name="recordType">Record-type discriminator; required, non-blank.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordSeq"/> or <paramref name="byteLength"/> is less than 1, or <paramref name="byteOffset"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="recordType"/> is null, empty, or whitespace.</exception>
    public RecordLocator(long recordSeq, long byteOffset, int byteLength, string recordType)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSeq, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfLessThan(byteLength, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);

        RecordSeq = recordSeq;
        ByteOffset = byteOffset;
        ByteLength = byteLength;
        RecordType = recordType;
    }
}
