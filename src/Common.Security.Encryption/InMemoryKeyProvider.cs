using System.Security.Cryptography;

namespace Common.Security.Encryption;

/// <summary>
/// In-memory <see cref="IKeyProvider"/> for development and testing. Generates a single random
/// active key on construction and resolves it by identity. Not for production — the real
/// implementation resolves wrapped keys from an HSM-backed Key Vault (deferred).
/// </summary>
public sealed class InMemoryKeyProvider : IKeyProvider
{
    private readonly Dictionary<string, DataKey> _keys = new(StringComparer.Ordinal);
    private readonly DataKey _active;

    /// <summary>Creates a provider with one freshly generated active key.</summary>
    public InMemoryKeyProvider()
    {
        var material = new byte[DataKey.MaterialLength];
        RandomNumberGenerator.Fill(material);

        _active = new DataKey(Guid.NewGuid().ToString("N"), "1", material);
        _keys[Compose(_active.KeyId, _active.KeyVersion)] = _active;
    }

    /// <inheritdoc />
    public DataKey GetActiveKey() => _active;

    /// <inheritdoc />
    public DataKey ResolveKey(string keyId, string keyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVersion);

        if (_keys.TryGetValue(Compose(keyId, keyVersion), out var key))
        {
            return key;
        }

        throw new KeyNotFoundException($"No data key for id '{keyId}' version '{keyVersion}'.");
    }

    private static string Compose(string keyId, string keyVersion) => $"{keyId}:{keyVersion}";
}
