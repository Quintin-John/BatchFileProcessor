namespace Common.Security.DataProtection;

/// <summary>
/// Supplies and resolves data-encryption keys (Option A envelope model): producers encrypt with
/// the active key; consumers resolve the key a ciphertext names by its id/version. Implementations
/// hold key material wrapped in an HSM-backed store — material never travels on the message bus.
/// </summary>
public interface IKeyProvider
{
    /// <summary>Returns the current active data key to encrypt new data with.</summary>
    DataKey GetActiveKey();

    /// <summary>Resolves a previously issued data key by identity, for decryption.</summary>
    /// <param name="keyId">Key identifier.</param>
    /// <param name="keyVersion">Key version.</param>
    /// <returns>The resolved data key.</returns>
    /// <exception cref="KeyNotFoundException">No key matches the given id and version.</exception>
    DataKey ResolveKey(string keyId, string keyVersion);
}
