using Common.FileIngestion.Layouts;
using Common.Security.DataProtection;

namespace Ingestion.Worker;

/// <summary>
/// Builds the field data-protection policy from the layout's per-field <c>encrypt</c> flags — the layout is
/// the single classification source. A flagged field is encrypted and redacted from logs; every other field
/// is clear. This is the composition-root bridge: the layout knows what to protect, the crypto knows how,
/// and neither depends on the other. Every layout field is classified by construction, so the fail-closed
/// policy lookup never faults on a field the layout defines.
/// </summary>
public static class LayoutProtectionPolicy
{
    /// <summary>Derives a <see cref="DataProtectionPolicy"/> from a layout's <c>encrypt</c> flags.</summary>
    /// <param name="layout">The layout whose fields carry the encrypt classification; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    public static DataProtectionPolicy From(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var fields = new Dictionary<string, FieldProtection>(StringComparer.Ordinal);
        foreach (var record in layout.RecordTypes)
        {
            foreach (var field in record.Fields)
            {
                fields[field.Name] = field.Encrypt
                    ? new FieldProtection(ProtectionAction.Encrypt, MaskStrategy: null, RedactInLogs: true)
                    : new FieldProtection(ProtectionAction.Clear, MaskStrategy: null, RedactInLogs: false);
            }
        }

        return new DataProtectionPolicy(fields);
    }
}
