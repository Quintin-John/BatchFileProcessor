using Common.FileIngestion.Layouts;
using Common.Security.DataProtection;

namespace Ingestion.Worker;

/// <summary>
/// Builds the field data-protection policy from the layout's per-field <c>encrypt</c> flags — the layout is
/// the single classification source. A flagged field is encrypted and redacted from logs; every other field
/// is clear. This is the composition-root bridge: the layout knows what to protect, the crypto knows how,
/// and neither depends on the other. Every layout field is classified by construction, so the fail-closed
/// policy lookup never faults on a field the layout defines. If a field name recurs across record types
/// with different classifications, construction fails closed rather than silently collapsing to one.
/// </summary>
public static class LayoutProtectionPolicy
{
    /// <summary>Derives a <see cref="DataProtectionPolicy"/> from a layout's <c>encrypt</c> flags.</summary>
    /// <param name="layout">The layout whose fields carry the encrypt classification; required. Any framing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    public static DataProtectionPolicy From(ILayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var fields = new Dictionary<string, FieldProtection>(StringComparer.Ordinal);
        foreach (var field in layout.DeclaredFields)
        {
            var protection = field.Encrypt
                ? new FieldProtection(ProtectionAction.Encrypt, MaskStrategy: null, RedactInLogs: true)
                : new FieldProtection(ProtectionAction.Clear, MaskStrategy: null, RedactInLogs: false);

            // Protection is keyed by field name across every record or row type. If one name is classified
            // two different ways, collapsing it would silently declassify one side (last write wins) —
            // fail closed at composition time rather than pick a winner.
            if (fields.TryGetValue(field.Name, out var existing) && existing != protection)
            {
                throw new InvalidOperationException(
                    $"Field '{field.Name}' has conflicting data-protection classifications across record " +
                    "types; a field name must resolve to a single classification.");
            }

            fields[field.Name] = protection;
        }

        return new DataProtectionPolicy(fields);
    }
}
