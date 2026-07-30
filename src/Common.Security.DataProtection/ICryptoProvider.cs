using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// A pluggable authenticated-encryption algorithm. Encrypts plaintext with a <see cref="DataKey"/>
/// into a self-describing <see cref="EncryptedValue"/> and reverses it. Additional data binds a
/// ciphertext to its context (anti-replay). Implementations wrap platform crypto — never a
/// home-grown cipher.
/// </summary>
public interface ICryptoProvider
{
    /// <summary>Algorithm identifier stamped into produced <see cref="EncryptedValue"/>s (e.g. <c>AES-256-GCM</c>).</summary>
    string Algorithm { get; }

    /// <summary>Encrypts <paramref name="plaintext"/> and returns a self-describing envelope.</summary>
    /// <param name="plaintext">Data to encrypt; must be non-empty.</param>
    /// <param name="key">Data key to encrypt with.</param>
    /// <param name="associatedData">Authenticated-but-not-encrypted context bound to the ciphertext.</param>
    EncryptedValue Encrypt(ReadOnlySpan<byte> plaintext, DataKey key, ReadOnlySpan<byte> associatedData);

    /// <summary>Decrypts an envelope, verifying integrity and the associated data.</summary>
    /// <param name="value">The envelope to decrypt.</param>
    /// <param name="key">Data key to decrypt with.</param>
    /// <param name="associatedData">The same context supplied at encryption time.</param>
    /// <returns>The recovered plaintext.</returns>
    byte[] Decrypt(EncryptedValue value, DataKey key, ReadOnlySpan<byte> associatedData);
}
