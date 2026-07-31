using System.Collections.ObjectModel;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// One record type in a layout: the discriminator value that identifies it plus its ordered fields, which
/// must contiguously cover the whole record (validated by <see cref="Layout"/>) — unless the record is
/// <see cref="Skip"/>ped, in which case it is a control record (e.g. a header or trailer) consumed for
/// framing but never emitted, so it needs no fields and is exempt from coverage.
/// </summary>
public sealed class RecordDefinition
{
    /// <summary>Logical name of the record type (e.g. a layout key).</summary>
    public string Name { get; }

    /// <summary>Discriminator value that identifies this record type in the data.</summary>
    public string Match { get; }

    /// <summary>Ordered field definitions. Defensively copied; read-only; empty only for a skipped record.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>
    /// Whether this record type is consumed for framing but never emitted upstream (header/trailer control
    /// records). Layout-driven; when true the record needs no fields and is exempt from coverage validation.
    /// </summary>
    public bool Skip { get; }

    /// <summary>Creates a validated record definition.</summary>
    /// <param name="name">Record-type name; required, non-blank.</param>
    /// <param name="match">Discriminator value; required, non-blank.</param>
    /// <param name="fields">Ordered fields; required, no null elements, non-empty unless <paramref name="skip"/>. Copied defensively.</param>
    /// <param name="skip">Whether the record is consumed but not emitted; defaults to false. A skipped record may have no fields.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/>/<paramref name="match"/> is blank, or <paramref name="fields"/> is empty (when not skipped) or contains a null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public RecordDefinition(string name, string match, IReadOnlyList<FieldDefinition> fields, bool skip = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(match);
        ArgumentNullException.ThrowIfNull(fields);

        if (!skip && fields.Count == 0)
        {
            throw new ArgumentException("A record type must define at least one field unless it is skipped.", nameof(fields));
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
        Skip = skip;
        Fields = new ReadOnlyCollection<FieldDefinition>(copy);
    }
}
