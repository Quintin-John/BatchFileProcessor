using System.Buffers;

namespace Common.FileIngestion.Batching;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> that measures how many UTF-8 bytes are written without materialising
/// the output. It hands back one reusable scratch buffer on every request and only accumulates the advanced
/// count, so a record's serialized size can be measured for the batch byte-cap without allocating (and then
/// discarding) a full byte array per record. Not thread-safe — one instance per single-threaded producer.
/// </summary>
internal sealed class ByteCountingBufferWriter : IBufferWriter<byte>
{
    private const int InitialBufferSize = 256;

    private byte[] _scratch = new byte[InitialBufferSize];

    /// <summary>Total bytes advanced since construction or the last <see cref="Reset"/>.</summary>
    public long BytesWritten { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        BytesWritten += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _scratch;
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _scratch;
    }

    /// <summary>Resets the running count so the writer can measure another value.</summary>
    public void Reset() => BytesWritten = 0;

    private void EnsureCapacity(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);

        // The buffer's contents are irrelevant (only the advanced count is kept), so the same scratch is
        // reused across requests; it only grows when a request needs more room than it currently has.
        var required = Math.Max(sizeHint, 1);
        if (_scratch.Length < required)
        {
            _scratch = new byte[required];
        }
    }
}
