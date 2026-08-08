namespace Common.FileIngestion.Abstractions;

/// <summary>
/// Frames a stream into records and computes the file's content hash in the same single pass. The Strategy
/// seam over framing, paired with <see cref="IRecordParser"/> over record content: a fixed-width
/// implementation frames by a constant stride, a delimited one by a terminator, and the pipeline is
/// indifferent to which — every record reports its own extent via <see cref="FramedRecord.ByteLength"/>.
/// Implementations must stream: memory is O(1) in file size, not O(file).
/// </summary>
public interface IRecordReader
{
    /// <summary>
    /// Reads <paramref name="stream"/> to completion, invoking <paramref name="onRecord"/> for each framed
    /// record in file order, and returns the file's content hash as an uppercase hex string (the FileId).
    /// </summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="onRecord">Async callback invoked per record; awaiting it applies backpressure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="onRecord"/> is null.</exception>
    /// <exception cref="InvalidDataException">The stream ends mid-record.</exception>
    Task<string> ReadAsync(
        Stream stream,
        Func<FramedRecord, CancellationToken, ValueTask> onRecord,
        CancellationToken cancellationToken);
}
