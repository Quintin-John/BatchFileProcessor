using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Encrypts an opaque payload that has no field-level classification — e.g. the raw content of a
/// record that failed parsing, which cannot be decomposed into policy-classified fields yet may hold
/// sensitive data (PAN/PII) and so must never travel in clear. Protection is unconditional (there is
/// nothing to classify); the ciphertext is bound to its context like any field. Distinct from
/// <see cref="IFieldProtector"/>, which is policy-driven per field.
/// </summary>
public interface IPayloadProtector
{
    /// <summary>Encrypts <paramref name="payload"/>, bound to <paramref name="context"/>.</summary>
    /// <param name="context">Context forming the anti-replay binding (AAD).</param>
    /// <param name="payload">The clear payload; required, non-empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="payload"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="payload"/> is empty.</exception>
    EncryptedFieldValue Protect(FieldProtectionContext context, string payload);

    /// <summary>Reverses <see cref="Protect"/>, recovering the clear payload.</summary>
    /// <param name="context">The same context supplied at protection time.</param>
    /// <param name="payload">The encrypted payload; required.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    string Unprotect(FieldProtectionContext context, EncryptedFieldValue payload);
}
