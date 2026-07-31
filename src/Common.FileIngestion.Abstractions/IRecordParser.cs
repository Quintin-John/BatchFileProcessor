namespace Common.FileIngestion.Abstractions;

/// <summary>
/// Parses one framed record into an <see cref="RecordParseResult"/>. The Strategy seam over record
/// framing — a fixed-length implementation exists now; a delimited one is added on first concrete need.
/// </summary>
public interface IRecordParser
{
    /// <summary>Parses a single record.</summary>
    /// <param name="recordSeq">1-based record sequence within the file.</param>
    /// <param name="byteOffset">Byte offset of the record within the file.</param>
    /// <param name="record">The framed record text.</param>
    RecordParseResult Parse(long recordSeq, long byteOffset, ReadOnlySpan<char> record);
}
