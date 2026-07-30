using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Reversible cryptographic protection of individual field values: encrypting or passing through per
/// classification, and reversing it. The single place field encryption is enforced, so it cannot be
/// applied inconsistently. Producing masked display forms is a separate concern — see
/// <see cref="IFieldMasker"/> — so a producer that only encrypts does not depend on masking.
/// </summary>
public interface IFieldProtector
{
    /// <summary>Protects a clear value per its field's classification (encrypt or pass through).</summary>
    /// <param name="context">Field context (also the anti-replay binding).</param>
    /// <param name="value">The value to protect.</param>
    /// <returns>An <see cref="EncryptedFieldValue"/> for encrypted fields, otherwise the value unchanged.</returns>
    /// <exception cref="KeyNotFoundException">The field is unclassified (fail-closed).</exception>
    FieldValue Protect(FieldProtectionContext context, FieldValue value);

    /// <summary>Reverses <see cref="Protect"/>: decrypts an encrypted value, or passes a clear value through.</summary>
    /// <param name="context">The same context supplied at protection time.</param>
    /// <param name="value">The value to unprotect.</param>
    FieldValue Unprotect(FieldProtectionContext context, FieldValue value);
}
