using System.Security.Cryptography;
using System.Text;
using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Default <see cref="IPayloadProtector"/>. Encrypts the UTF-8 bytes of a payload with the active key
/// via the configured <see cref="ICryptoProvider"/>, binding the ciphertext to its context with the
/// shared <see cref="ProtectionAad"/> encoder. No policy lookup — a payload with no field structure is
/// always protected.
/// </summary>
public sealed class DefaultPayloadProtector : IPayloadProtector
{
    private readonly ICryptoProvider _crypto;
    private readonly IKeyProvider _keys;

    /// <summary>Creates the protector.</summary>
    /// <param name="crypto">Crypto provider.</param>
    /// <param name="keys">Key provider.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public DefaultPayloadProtector(ICryptoProvider crypto, IKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(keys);

        _crypto = crypto;
        _keys = keys;
    }

    /// <inheritdoc />
    public EncryptedFieldValue Protect(FieldProtectionContext context, string payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(payload); // AEAD of empty plaintext yields no ciphertext to protect

        var plaintext = Encoding.UTF8.GetBytes(payload);
        try
        {
            var envelope = _crypto.Encrypt(plaintext, _keys.GetActiveKey(), ProtectionAad.Build(context));
            return new EncryptedFieldValue(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <inheritdoc />
    public string Unprotect(FieldProtectionContext context, EncryptedFieldValue payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(payload);

        var key = _keys.ResolveKey(payload.Value.KeyId, payload.Value.KeyVersion);
        var plaintext = _crypto.Decrypt(payload.Value, key, ProtectionAad.Build(context));
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            // Zero the transient cleartext so a decrypted payload never lingers on the heap
            // (symmetric with Protect).
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
