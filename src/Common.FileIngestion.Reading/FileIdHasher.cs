using System.Buffers;

namespace Common.FileIngestion.Reading;

/// <summary>
/// Computes a file's identity as the SHA-256 of its full content, via the shared
/// <see cref="FileContentHash"/> definition so it is byte-for-byte identical to the digest
/// <see cref="StreamRecordReader"/> produces on the read pass. Used for the pre-read pass that
/// establishes the FileId before any batch is published — the read pass then recomputes it as an
/// integrity guard.
/// </summary>
public static class FileIdHasher
{
    /// <summary>Reads the stream to the end and returns its content hash as uppercase hex.</summary>
    /// <param name="stream">The source stream; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public static async Task<string> ComputeAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var hash = FileContentHash.CreateIncremental();
        var buffer = ArrayPool<byte>.Shared.Rent(FileContentHash.StreamBufferBytes);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            // The buffer held file content (potentially PAN/PII) and returns to a shared pool.
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        return FileContentHash.Format(hash.GetHashAndReset());
    }
}
