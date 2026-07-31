using System.Security.Cryptography;

namespace Common.FileIngestion.Reading;

/// <summary>
/// The single definition of a file's content hash: the algorithm and hex formatting shared by the
/// pre-read pass (<see cref="FileIdHasher"/>) and the read pass (<see cref="StreamRecordReader"/>).
/// Both passes must produce a byte-identical FileId for the resume integrity guard to hold, so the
/// algorithm and casing live here once instead of being restated (and able to drift) at each call site.
/// </summary>
internal static class FileContentHash
{
    /// <summary>Buffer size for streaming a file through the hasher; the framework's default stream-copy
    /// size (80 KiB), which keeps hashing memory O(1) in file size.</summary>
    internal const int StreamBufferBytes = 81920;

    /// <summary>The content-hash algorithm. Changing it here changes both passes together.</summary>
    internal static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>Creates an incremental hasher over the shared <see cref="Algorithm"/>.</summary>
    internal static IncrementalHash CreateIncremental() => IncrementalHash.CreateHash(Algorithm);

    /// <summary>Formats a raw digest as the canonical FileId string (uppercase hex).</summary>
    internal static string Format(byte[] hash) => Convert.ToHexString(hash);
}
