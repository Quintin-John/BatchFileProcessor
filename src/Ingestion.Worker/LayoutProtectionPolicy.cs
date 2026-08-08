using Common.FileIngestion.Layouts;
using Common.Security.DataProtection;

namespace Ingestion.Worker;

/// <summary>
/// Builds the field data-protection policy from the layout's per-field <c>encrypt</c> flags — the layout is
/// the single source of what is sensitive. A flagged field is encrypted; every other field is clear. This
/// is the composition-root bridge: the layout knows what to protect, the crypto knows how, and neither
/// depends on the other. Every layout field lands in the policy, so the fail-closed lookup never faults on a
/// field the layout defines. If one name is flagged in one record type and not in another, construction
/// fails rather than silently picking a winner.
/// </summary>
public static class LayoutProtectionPolicy
{
    /// <summary>Derives a <see cref="DataProtectionPolicy"/> from a layout's <c>encrypt</c> flags.</summary>
    /// <param name="layout">The layout whose fields carry the encrypt flags; required. Any framing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    public static DataProtectionPolicy From(ILayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var fields = new Dictionary<string, ProtectionAction>(StringComparer.Ordinal);
        foreach (var field in layout.DeclaredFields)
        {
            var protection = field.Encrypt ? ProtectionAction.Encrypt : ProtectionAction.Clear;

            // Keyed by field name across every record or row type. If one name is flagged in one place and
            // not another, collapsing it would silently stop encrypting one side (last write wins) — fail at
            // composition time rather than pick a winner.
            if (fields.TryGetValue(field.Name, out var existing) && existing != protection)
            {
                throw new InvalidOperationException(
                    $"Field '{field.Name}' is marked encrypt in one record type but not in another; a " +
                    "field name must be encrypted everywhere it appears or nowhere.");
            }

            fields[field.Name] = protection;
        }

        return new DataProtectionPolicy(fields);
    }
}
