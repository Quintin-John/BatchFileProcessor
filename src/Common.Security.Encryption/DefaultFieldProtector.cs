using System.Security.Cryptography;
using System.Text.Json;
using Common.Messaging.Contracts;

namespace Common.Security.Encryption;

/// <summary>
/// Default <see cref="IFieldProtector"/>. Encrypts clear values with the active key via the
/// configured <see cref="ICryptoProvider"/>, binding each ciphertext to its
/// <see cref="FieldProtectionContext"/>. Clear values are serialized through the messaging
/// contract's JSON so typed values (string, number, boolean, null) round-trip losslessly.
/// </summary>
public sealed class DefaultFieldProtector : IFieldProtector
{
    private readonly ICryptoProvider _crypto;
    private readonly IKeyProvider _keys;
    private readonly EncryptionPolicy _policy;

    /// <summary>Creates the protector.</summary>
    /// <param name="crypto">Crypto provider.</param>
    /// <param name="keys">Key provider.</param>
    /// <param name="policy">Data-protection policy.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public DefaultFieldProtector(ICryptoProvider crypto, IKeyProvider keys, EncryptionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(policy);

        _crypto = crypto;
        _keys = keys;
        _policy = policy;
    }

    /// <inheritdoc />
    public FieldValue Protect(FieldProtectionContext context, FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(value);

        var protection = _policy.GetProtection(context.Field);
        if (protection != ProtectionAction.Encrypt || value is EncryptedFieldValue)
        {
            return value;
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, MessagingJson.Options);
        try
        {
            var envelope = _crypto.Encrypt(plaintext, _keys.GetActiveKey(), ProtectionAad.Build(context));
            return new EncryptedFieldValue(envelope);
        }
        finally
        {
            // Zero the transient cleartext so a decrypted field value never lingers on the heap.
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <inheritdoc />
    public FieldValue Unprotect(FieldProtectionContext context, FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(value);

        if (value is not EncryptedFieldValue encrypted)
        {
            return value;
        }

        var key = _keys.ResolveKey(encrypted.Value.KeyId, encrypted.Value.KeyVersion);
        var plaintext = _crypto.Decrypt(encrypted.Value, key, ProtectionAad.Build(context));
        try
        {
            return JsonSerializer.Deserialize<FieldValue>(plaintext, MessagingJson.Options)
                ?? throw new InvalidOperationException("Decrypted field value was null.");
        }
        finally
        {
            // Zero the transient cleartext so a decrypted field value never lingers on the heap
            // (symmetric with Protect).
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
