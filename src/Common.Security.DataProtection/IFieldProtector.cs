using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Reversible encryption of individual field values: encrypting or passing through per the policy, and
/// reversing it. The single place field encryption is enforced, so it cannot be applied inconsistently.
/// </summary>
public interface IFieldProtector
{
    /// <summary>Encrypts a clear value, or passes it through when the policy does not encrypt that field.</summary>
    /// <param name="context">Field context (also the anti-replay binding).</param>
    /// <param name="value">The value to protect.</param>
    /// <returns>An <see cref="EncryptedFieldValue"/> for encrypted fields, otherwise the value unchanged.</returns>
    /// <exception cref="KeyNotFoundException">The policy does not cover the field (fail-closed).</exception>
    FieldValue Protect(FieldProtectionContext context, FieldValue value);

    /// <summary>Reverses <see cref="Protect"/>: decrypts an encrypted value, or passes a clear value through.</summary>
    /// <param name="context">The same context supplied at protection time.</param>
    /// <param name="value">The value to unprotect.</param>
    FieldValue Unprotect(FieldProtectionContext context, FieldValue value);
}
