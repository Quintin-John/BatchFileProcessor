using System.Collections.ObjectModel;

namespace Common.Security.DataProtection;

/// <summary>
/// The security-owned data-protection policy: how each field is protected. Field lookup is
/// fail-closed — an unclassified field throws rather than defaulting to clear, so a new field
/// can never silently leak.
/// </summary>
public sealed class DataProtectionPolicy
{
    /// <summary>Per-field protection, keyed by layout field name (ordinal). Read-only.</summary>
    public IReadOnlyDictionary<string, FieldProtection> Fields { get; }

    /// <summary>Creates a policy from a per-field map. The map is defensively copied.</summary>
    /// <param name="fields">Field-name to protection; required, non-null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public DataProtectionPolicy(IReadOnlyDictionary<string, FieldProtection> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var copy = new Dictionary<string, FieldProtection>(fields.Count, StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            copy[pair.Key] = pair.Value;
        }

        Fields = new ReadOnlyDictionary<string, FieldProtection>(copy);
    }

    /// <summary>Returns the protection for a field, or throws if the field is unclassified (fail-closed).</summary>
    /// <param name="field">Field name.</param>
    /// <exception cref="KeyNotFoundException">The field has no classification.</exception>
    public FieldProtection GetProtection(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (Fields.TryGetValue(field, out var protection))
        {
            return protection;
        }

        throw new KeyNotFoundException($"Field '{field}' has no data-protection classification.");
    }

    /// <summary>Attempts to get the protection for a field.</summary>
    /// <param name="field">Field name.</param>
    /// <param name="protection">The protection, if classified.</param>
    /// <returns>True if the field is classified.</returns>
    public bool TryGetProtection(string field, out FieldProtection? protection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return Fields.TryGetValue(field, out protection);
    }
}
