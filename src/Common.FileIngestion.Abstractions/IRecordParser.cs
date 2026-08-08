namespace Common.FileIngestion.Abstractions;

/// <summary>
/// Parses one framed record into an <see cref="RecordParseResult"/>. The Strategy seam over record
/// framing — a fixed-length implementation exists now; a delimited one is added on first concrete need.
/// </summary>
public interface IRecordParser
{
    /// <summary>Parses a single framed record.</summary>
    /// <param name="framed">The framed record: its position, extent, and decoded content.</param>
    RecordParseResult Parse(FramedRecord framed);
}
