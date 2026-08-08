using System.Collections.ObjectModel;

namespace Common.Security.Encryption;

/// <summary>
/// Which fields are encrypted on the wire. That is the whole question it answers: a value is encrypted or it
/// is carried in clear, so a field maps straight to its <see cref="ProtectionAction"/> with nothing wrapping
/// it. Lookup is fail-closed — a field the policy has never heard of throws rather than defaulting to clear,
/// so a newly added field cannot leak by being forgotten.
/// </summary>
public sealed class EncryptionPolicy
{
    /// <summary>Per-field action, keyed by layout field name (ordinal). Read-only.</summary>
    public IReadOnlyDictionary<string, ProtectionAction> Fields { get; }

    /// <summary>Creates a policy from a per-field map. The map is defensively copied.</summary>
    /// <param name="fields">Field-name to action; required, non-null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public EncryptionPolicy(IReadOnlyDictionary<string, ProtectionAction> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var copy = new Dictionary<string, ProtectionAction>(fields.Count, StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            copy[pair.Key] = pair.Value;
        }

        Fields = new ReadOnlyDictionary<string, ProtectionAction>(copy);
    }

    /// <summary>
    /// Returns whether a field is encrypted, or throws if the policy does not cover it.
    /// <para>
    /// Deliberately the only lookup. A Try- form would have to hand back a default on failure, and the
    /// default of <see cref="ProtectionAction"/> is <see cref="ProtectionAction.Clear"/> — a caller that
    /// ignored the return value would send an unknown field in clear. There is no safe default here, so
    /// there is no way to ask that does not fail closed.
    /// </para>
    /// </summary>
    /// <param name="field">Field name.</param>
    /// <exception cref="KeyNotFoundException">The policy does not cover the field.</exception>
    public ProtectionAction GetProtection(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (Fields.TryGetValue(field, out var protection))
        {
            return protection;
        }

        throw new KeyNotFoundException(
            $"Field '{field}' is not in the policy, so nothing says whether it must be encrypted.");
    }
}
