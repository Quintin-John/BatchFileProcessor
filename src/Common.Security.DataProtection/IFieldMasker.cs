using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Produces a masked, safe-to-display form of a clear field value for diagnostics, per the field's
/// mask strategy. Separate from <see cref="IFieldProtector"/> (encryption) so a consumer that only
/// encrypts does not depend on masking, and masking can change without touching the crypto path.
/// </summary>
public interface IFieldMasker
{
    /// <summary>Produces a masked, safe-to-display form of a clear value.</summary>
    /// <param name="context">Field context (selects the masking strategy).</param>
    /// <param name="value">The clear value to mask.</param>
    /// <exception cref="KeyNotFoundException">The field is unclassified (fail-closed).</exception>
    /// <exception cref="InvalidOperationException">The value is encrypted, or its mask strategy is unknown.</exception>
    string Mask(FieldProtectionContext context, FieldValue value);
}
