using Common.FileIngestion.Abstractions;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;

namespace Common.FileIngestion.Reading;

/// <summary>
/// Frames a stream into fixed-width records and computes the file's SHA-256 in a single streaming
/// pass. Memory is O(1) in file size — records flow through a bounded read buffer, never the whole
/// file. Handles records split across read segments and a final record with no trailing terminator.
/// </summary>
public sealed class StreamRecordReader : IRecordReader
{
    private readonly int _recordLength;
    private readonly int _terminatorLength;
    private readonly Encoding _encoding;

    /// <summary>Creates a reader for fixed records of <paramref name="recordLength"/> bytes.</summary>
    /// <param name="recordLength">Record length in bytes; must be at least 1.</param>
    /// <param name="terminatorLength">Terminator length in bytes (e.g. 1 for LF, 0 for none); non-negative.</param>
    /// <param name="encoding">Single-byte encoding used to decode record content.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordLength"/> is less than 1 or <paramref name="terminatorLength"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is null.</exception>
    public StreamRecordReader(int recordLength, int terminatorLength, Encoding encoding)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordLength, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(terminatorLength);
        ArgumentNullException.ThrowIfNull(encoding);
        if (!encoding.IsSingleByte)
        {
            throw new ArgumentException(
                "A single-byte encoding is required so a record's byte length equals its character length; " +
                "a multi-byte encoding would misalign fixed-width fields.",
                nameof(encoding));
        }

        _recordLength = recordLength;
        _terminatorLength = terminatorLength;
        _encoding = encoding;
    }

    /// <summary>Record content length in bytes (excludes the terminator).</summary>
    public int RecordLength => _recordLength;

    /// <inheritdoc />
    /// <remarks>Frames by a constant stride, so every record reports the same extent except a final record
    /// with no trailing terminator, which reports its content length only.</remarks>
    public async Task<string> ReadAsync(
        Stream stream,
        Func<FramedRecord, CancellationToken, ValueTask> onRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(onRecord);

        var stride = _recordLength + _terminatorLength;
        using var hash = FileContentHash.CreateIncremental();
        var scratch = ArrayPool<byte>.Shared.Rent(stride);
        var pipe = PipeReader.Create(stream);
        long recordSeq = 1;
        long byteOffset = 0;

        try
        {
            while (true)
            {
                var result = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                long consumed = 0;

                while (buffer.Length - consumed >= stride)
                {
                    var content = TakeRecord(buffer.Slice(consumed, stride), hash, scratch);
                    await onRecord(new FramedRecord(recordSeq, byteOffset, stride, content), cancellationToken).ConfigureAwait(false);
                    consumed += stride;
                    recordSeq++;
                    byteOffset += stride;
                }

                if (result.IsCompleted)
                {
                    var remaining = buffer.Length - consumed;
                    string? finalContent = null;
                    if (remaining == _recordLength)
                    {
                        finalContent = TakeRecord(buffer.Slice(consumed, _recordLength), hash, scratch);
                    }
                    else if (remaining != 0)
                    {
                        pipe.AdvanceTo(buffer.End);
                        throw new InvalidDataException($"File ends with an incomplete record ({remaining} trailing bytes).");
                    }

                    pipe.AdvanceTo(buffer.End);
                    if (finalContent is not null)
                    {
                        // A final record with no trailing terminator consumes only its content bytes, so its
                        // extent is RecordLength, not Stride — a resume point must not run past end of file.
                        await onRecord(new FramedRecord(recordSeq, byteOffset, _recordLength, finalContent), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    break;
                }

                pipe.AdvanceTo(buffer.GetPosition(consumed), buffer.End);
            }
        }
        finally
        {
            // Zero on return: the scratch buffer held cleartext record bytes and goes back to a shared
            // pool that any other component can rent.
            ArrayPool<byte>.Shared.Return(scratch, clearArray: true);
            await pipe.CompleteAsync().ConfigureAwait(false);
        }

        return FileContentHash.Format(hash.GetHashAndReset());
    }

    // Copies the (record[+terminator]) slice into scratch, hashes all its bytes, and decodes the
    // record portion. `slice` is a ReadOnlySequence (not a ref struct), so this stays outside awaits.
    private string TakeRecord(ReadOnlySequence<byte> slice, IncrementalHash hash, byte[] scratch)
    {
        var length = (int)slice.Length;
        slice.CopyTo(scratch.AsSpan(0, length));
        hash.AppendData(scratch.AsSpan(0, length));
        return _encoding.GetString(scratch.AsSpan(0, _recordLength));
    }
}
