namespace Common.FileIngestion.Abstractions;

/// <summary>One framed record: its 1-based sequence, byte offset in the file, byte extent, and decoded content.</summary>
/// <param name="RecordSeq">1-based record sequence.</param>
/// <param name="ByteOffset">Byte offset of the record's first byte within the file.</param>
/// <param name="ByteLength">
/// Total bytes this record consumes in the file, including any record terminator. The reader is the
/// authority on it: <c>ByteOffset + ByteLength</c> is the next record's offset, so a resume point derived
/// from it is correct whether records are fixed-width or variable-length.
/// </param>
/// <param name="Content">The decoded record text (record bytes only, excluding any terminator).</param>
/// <param name="RowType">
/// The record type, when framing is what determines it. Delimited rows are classified by position — the
/// first rows are the header, the last are the trailer — and only the reader knows a row's position, so it
/// resolves the type and states it here. Null when the record's own content carries the discriminator, as
/// in fixed-width framing, where the parser resolves it instead.
/// </param>
public readonly record struct FramedRecord(
    long RecordSeq,
    long ByteOffset,
    int ByteLength,
    string Content,
    string? RowType = null);
