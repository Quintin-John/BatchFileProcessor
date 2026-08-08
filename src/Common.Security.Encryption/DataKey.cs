namespace Common.Security.Encryption;

/// <summary>
/// A symmetric data-encryption key (DEK) plus the identity by which it is resolved.
/// Holds 32 bytes of key material for AES-256. The material is defensively copied and
/// exposed only within this library.
/// </summary>
public sealed class DataKey
{
    /// <summary>Required key-material length for AES-256, in bytes.</summary>
    public const int MaterialLength = 32;

    private readonly byte[] _material;

    /// <summary>Identifier by which this key is resolved (e.g. a Key Vault key id).</summary>
    public string KeyId { get; }

    /// <summary>Version of the key, so a rotated key resolves deterministically.</summary>
    public string KeyVersion { get; }

    /// <summary>The raw key material. Internal — never part of the public surface.</summary>
    internal ReadOnlySpan<byte> Material => _material;

    /// <summary>Creates a data key.</summary>
    /// <param name="keyId">Key identifier; required, non-blank.</param>
    /// <param name="keyVersion">Key version; required, non-blank.</param>
    /// <param name="material">Exactly <see cref="MaterialLength"/> bytes; copied defensively.</param>
    /// <exception cref="ArgumentException"><paramref name="keyId"/>/<paramref name="keyVersion"/> is blank, or <paramref name="material"/> is not <see cref="MaterialLength"/> bytes.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is null.</exception>
    public DataKey(string keyId, string keyVersion, byte[] material)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVersion);
        ArgumentNullException.ThrowIfNull(material);

        if (material.Length != MaterialLength)
        {
            throw new ArgumentException($"Data key material must be exactly {MaterialLength} bytes.", nameof(material));
        }

        KeyId = keyId;
        KeyVersion = keyVersion;
        _material = (byte[])material.Clone();
    }
}
