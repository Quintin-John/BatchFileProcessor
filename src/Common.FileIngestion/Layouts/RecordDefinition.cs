using System.Collections.ObjectModel;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// One record type in a layout: the discriminator value that identifies it plus its ordered
/// fields, which must contiguously cover the whole record (validated by <see cref="Layout"/>).
/// </summary>
public sealed class RecordDefinition
{
    /// <summary>Logical name of the record type (e.g. a layout key).</summary>
    public string Name { get; }

    /// <summary>Discriminator value that identifies this record type in the data.</summary>
    public string Match { get; }

    /// <summary>Ordered field definitions. Defensively copied; read-only; never empty.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>Creates a validated record definition.</summary>
    /// <param name="name">Record-type name; required, non-blank.</param>
    /// <param name="match">Discriminator value; required, non-blank.</param>
    /// <param name="fields">Ordered fields; required, non-empty, no null elements. Copied defensively.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/>/<paramref name="match"/> is blank, or <paramref name="fields"/> is empty or contains a null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public RecordDefinition(string name, string match, IReadOnlyList<FieldDefinition> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(match);
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
        {
            throw new ArgumentException("A record type must define at least one field.", nameof(fields));
        }

        var copy = new List<FieldDefinition>(fields.Count);
        foreach (var field in fields)
        {
            if (field is null)
            {
                throw new ArgumentException("Fields must not contain null elements.", nameof(fields));
            }

            copy.Add(field);
        }

        Name = name;
        Match = match;
        Fields = new ReadOnlyCollection<FieldDefinition>(copy);
    }
}
