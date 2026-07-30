using System.Globalization;
using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Default <see cref="IFieldMasker"/>. Resolves the field's mask strategy from the policy and applies
/// the matching <see cref="IMasker"/> to the value's clear string form. A field with no mask strategy
/// is returned in clear; an already-encrypted value cannot be masked.
/// </summary>
public sealed class DefaultFieldMasker : IFieldMasker
{
    private readonly DataProtectionPolicy _policy;
    private readonly Dictionary<string, IMasker> _maskers;

    /// <summary>Creates the masker.</summary>
    /// <param name="policy">Data-protection policy (supplies each field's mask strategy).</param>
    /// <param name="maskers">Available maskers, keyed by their strategy name.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public DefaultFieldMasker(DataProtectionPolicy policy, IEnumerable<IMasker> maskers)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(maskers);

        _policy = policy;
        _maskers = maskers.ToDictionary(m => m.Name, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string Mask(FieldProtectionContext context, FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(value);

        var protection = _policy.GetProtection(context.Field);
        var clear = ClearString(value);

        if (protection.MaskStrategy is null)
        {
            return clear;
        }

        if (!_maskers.TryGetValue(protection.MaskStrategy, out var masker))
        {
            throw new InvalidOperationException($"Unknown mask strategy '{protection.MaskStrategy}'.");
        }

        return masker.Mask(clear);
    }

    private static string ClearString(FieldValue value) => value switch
    {
        EncryptedFieldValue => throw new InvalidOperationException("Cannot mask an already-encrypted value."),
        ClearFieldValue { Value: null } => string.Empty,
        ClearFieldValue { Value: string s } => s,
        ClearFieldValue clear => Convert.ToString(clear.Value, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => throw new InvalidOperationException($"Unsupported field value type '{value.GetType()}'."),
    };
}
