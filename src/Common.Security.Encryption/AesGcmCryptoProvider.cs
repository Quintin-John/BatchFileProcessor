using System.Security.Cryptography;
using Common.Messaging.Contracts;

namespace Common.Security.Encryption;

/// <summary>
/// AES-256-GCM authenticated encryption (FIPS 197 / NIST SP 800-38D). Confidentiality and
/// integrity in one pass; a fresh random 96-bit nonce per call. Wraps
/// <see cref="System.Security.Cryptography.AesGcm"/> — no home-grown crypto.
/// </summary>
public sealed class AesGcmCryptoProvider : ICryptoProvider
{
    private const int NonceLength = 12; // 96-bit nonce, the GCM standard
    private const int TagLength = 16;   // 128-bit authentication tag

    /// <inheritdoc />
    public string Algorithm => "AES-256-GCM";

    /// <inheritdoc />
    public EncryptedValue Encrypt(ReadOnlySpan<byte> plaintext, DataKey key, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("Plaintext must be non-empty.", nameof(plaintext));
        }

        Span<byte> nonce = stackalloc byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        Span<byte> tag = stackalloc byte[TagLength];

        using var aes = new AesGcm(key.Material, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedValue(
            Algorithm,
            key.KeyId,
            key.KeyVersion,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(tag));
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The envelope's algorithm is not this provider's.</exception>
    /// <exception cref="CryptographicException">Integrity check fails (tampering, wrong key, or wrong associated data).</exception>
    public byte[] Decrypt(EncryptedValue value, DataKey key, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);

        if (!string.Equals(value.Algorithm, Algorithm, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Algorithm mismatch: envelope is '{value.Algorithm}', provider is '{Algorithm}'.");
        }

        byte[] nonce;
        byte[] ciphertext;
        byte[] tag;
        try
        {
            nonce = Convert.FromBase64String(value.Nonce);
            ciphertext = Convert.FromBase64String(value.Ciphertext);
            tag = Convert.FromBase64String(value.Tag);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Encrypted value contains invalid base64.", ex);
        }

        // Nonce and tag sizes are pinned crypto parameters — never taken from the (untrusted)
        // envelope, so a shortened tag cannot downgrade authentication strength.
        if (nonce.Length != NonceLength || tag.Length != TagLength)
        {
            throw new CryptographicException("Encrypted value has an invalid nonce or tag length.");
        }

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key.Material, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }
}
