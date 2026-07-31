using System.Security.Cryptography;

namespace Common.FileIngestion.Reading;

/// <summary>
/// Computes a file's identity as the SHA-256 of its full content, formatted identically to
/// <see cref="StreamRecordReader"/> (uppercase hex). Used for the pre-read pass that establishes the
/// FileId before any batch is published — the read pass then recomputes it as an integrity guard.
/// </summary>
public static class FileIdHasher
{
    /// <summary>Reads the stream to the end and returns its SHA-256 as uppercase hex.</summary>
    /// <param name="stream">The source stream; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public static async Task<string> ComputeAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
