namespace Common.Messaging.Contracts;

/// <summary>
/// Self-describing ciphertext envelope for a single encrypted field value.
/// Carries everything a consumer needs to select the correct algorithm and key
/// to decrypt, so that algorithm and key rotation are non-breaking.
/// Binary members (<see cref="Nonce"/>, <see cref="Ciphertext"/>, <see cref="Tag"/>)
/// are base64-encoded for transport.
/// </summary>
public sealed record EncryptedValue
{
    /// <summary>Algorithm the value was encrypted with (e.g. <c>AES-256-GCM</c>).</summary>
    public string Algorithm { get; }

    /// <summary>Identifier of the key used (e.g. a Key Vault key identifier).</summary>
    public string KeyId { get; }

    /// <summary>Version of the key, so a rotated key resolves deterministically.</summary>
    public string KeyVersion { get; }

    /// <summary>Base64-encoded nonce/IV for this value.</summary>
    public string Nonce { get; }

    /// <summary>Base64-encoded ciphertext.</summary>
    public string Ciphertext { get; }

    /// <summary>Base64-encoded AEAD authentication tag.</summary>
    public string Tag { get; }

    /// <summary>
    /// Creates a validated ciphertext envelope. Every member is required and must be
    /// non-null and non-blank.
    /// </summary>
    /// <param name="algorithm">Algorithm identifier (e.g. <c>AES-256-GCM</c>).</param>
    /// <param name="keyId">Key identifier.</param>
    /// <param name="keyVersion">Key version.</param>
    /// <param name="nonce">Base64-encoded nonce/IV.</param>
    /// <param name="ciphertext">Base64-encoded ciphertext.</param>
    /// <param name="tag">Base64-encoded authentication tag.</param>
    /// <exception cref="ArgumentException">Any argument is null, empty, or whitespace.</exception>
    public EncryptedValue(
        string algorithm,
        string keyId,
        string keyVersion,
        string nonce,
        string ciphertext,
        string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        Algorithm = algorithm;
        KeyId = keyId;
        KeyVersion = keyVersion;
        Nonce = nonce;
        Ciphertext = ciphertext;
        Tag = tag;
    }
}
