using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Applies the data-protection policy to individual field values: encrypting or passing through
/// per classification, reversing it, and producing masked forms for diagnostics. The single
/// place field protection is enforced, so it cannot be applied inconsistently.
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

    /// <summary>Produces a masked, safe-to-display form of a clear value for diagnostics.</summary>
    /// <param name="context">Field context (selects the masking strategy).</param>
    /// <param name="value">The clear value to mask.</param>
    /// <exception cref="KeyNotFoundException">The field is unclassified (fail-closed).</exception>
    /// <exception cref="InvalidOperationException">The value is encrypted, or its mask strategy is unknown.</exception>
    string Mask(FieldProtectionContext context, FieldValue value);
}
