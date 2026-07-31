using System.Security.Cryptography;
using System.Text;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Derives a stable <see cref="Guid"/> from a byte-stable name, so the same logical message always maps to
/// the same transport envelope id. The transport envelope's MessageId/CorrelationId are <see cref="Guid"/>-
/// typed while the domain ids are strings; this bridge lets a broker (or MassTransit's inbox) deduplicate a
/// replay keyed on the envelope id without changing the domain id scheme. SHA-256 (not SHA-1) is used so no
/// weak-hash rule is tripped; only determinism is required, not a specific UUID version.
/// </summary>
internal static class DeterministicGuid
{
    private const int GuidByteLength = 16;

    /// <summary>Maps a name to a deterministic GUID (same name always yields the same GUID).</summary>
    /// <param name="name">The stable name; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    public static Guid From(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), digest);
        return new Guid(digest[..GuidByteLength]);
    }
}
