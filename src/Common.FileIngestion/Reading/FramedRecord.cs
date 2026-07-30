namespace Common.FileIngestion.Reading;

/// <summary>One framed record: its 1-based sequence, byte offset in the file, and decoded content.</summary>
/// <param name="RecordSeq">1-based record sequence.</param>
/// <param name="ByteOffset">Byte offset of the record's first byte within the file.</param>
/// <param name="Content">The decoded record text (record bytes only, excluding any terminator).</param>
public readonly record struct FramedRecord(long RecordSeq, long ByteOffset, string Content);
