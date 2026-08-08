using Common.FileIngestion.Layouts;

namespace Ingestion.Worker;

/// <summary>
/// Collects the field names a layout marks <c>encrypt</c>, unioned across every record type and every
/// profile's layout. This is the layout-driven source of truth for which structured log keys must be
/// redacted — no field names are hardcoded; the set is derived entirely from the layouts' encrypt flags.
/// </summary>
internal static class SensitiveFieldNames
{
    /// <summary>Returns the distinct names of all <c>encrypt</c>-flagged fields across the given layouts.</summary>
    /// <param name="layouts">The loaded layouts; required, no null elements. Any framing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layouts"/> or a contained layout is null.</exception>
    public static IReadOnlySet<string> From(IEnumerable<ILayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in layouts)
        {
            ArgumentNullException.ThrowIfNull(layout);
            foreach (var field in layout.DeclaredFields)
            {
                if (field.Encrypt)
                {
                    names.Add(field.Name);
                }
            }
        }

        return names;
    }
}
