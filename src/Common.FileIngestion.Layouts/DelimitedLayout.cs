using System.Collections.ObjectModel;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// A validated delimited layout: the field delimiter, the encoding, and the row types. Construction is
/// fail-closed — a <see cref="DelimitedLayout"/> cannot exist in an invalid state. Generic: the
/// format-specific detail lives entirely in the source data, not here, so a new delimited feed is a new
/// layout file and no code change.
/// <para>
/// Row types are identified positionally first: the first <see cref="DelimitedRowDefinition.Rows"/> rows
/// belong to the header type, the last belong to the trailer type, and the body takes the remainder. Where a
/// fixed-width <see cref="Layout"/> identifies a record by a discriminator at a byte position, a delimited
/// file has no guaranteed discriminator column — so a body of a single type needs none, and a body mixing
/// several declares the column its rows name themselves in.
/// </para>
/// </summary>
public sealed class DelimitedLayout : ILayout
{
    private readonly Dictionary<string, DelimitedRowDefinition> _byName;

    /// <summary>Layout version identifier.</summary>
    public string Version { get; }

    /// <summary>
    /// The text separating fields within a row. Not restricted to one character: a feed separated by
    /// <c>~|~</c> is as ordinary as one separated by a comma, and neither requires a code change.
    /// </summary>
    public string Delimiter { get; }

    /// <summary>
    /// The character a row ends with. Declared, not assumed: a feed framed on CR alone, or on an ASCII
    /// record separator, is as valid as one framed on LF and must not require a code change.
    /// </summary>
    public char RowTerminator { get; }

    /// <summary>Character encoding name (single-byte).</summary>
    public string Encoding { get; }

    /// <summary>The row types. Defensively copied; read-only.</summary>
    public IReadOnlyList<DelimitedRowDefinition> RowTypes { get; }

    /// <inheritdoc />
    public IEnumerable<LayoutField> DeclaredFields =>
        RowTypes.SelectMany(row => row.Fields)
                .Select(definition => new LayoutField(definition.Name, definition.Encrypt));

    /// <summary>
    /// The row types spanning the body of the file; at least one always exists. More than one is allowed
    /// when each names itself with a marker, so a feed whose body mixes record types needs a layout edit and
    /// no code change.
    /// </summary>
    public IReadOnlyList<DelimitedRowDefinition> DataRows { get; }

    /// <summary>
    /// The field index carrying the marker that tells one body row type from another, or null when a single
    /// data type spans the body and position alone identifies it. Shared by every data type, so a body row
    /// resolves by reading one column rather than by trying each type in turn — which would make the answer
    /// depend on declaration order.
    /// </summary>
    public int? DataMarkerIndex { get; }

    /// <summary>The leading row type, or null when the file has no header.</summary>
    public DelimitedRowDefinition? Header { get; }

    /// <summary>The trailing row type, or null when the file has no trailer.</summary>
    public DelimitedRowDefinition? Trailer { get; }

    /// <summary>Rows claimed by the header type; 0 when there is no header.</summary>
    public int HeaderRows => Header?.Rows ?? 0;

    /// <summary>Rows claimed by the trailer type; 0 when there is no trailer.</summary>
    public int TrailerRows => Trailer?.Rows ?? 0;

    /// <summary>Creates a validated layout. All structural invariants are enforced here.</summary>
    /// <param name="version">Version identifier; required, non-blank.</param>
    /// <param name="delimiter">The resolved field delimiter; required, non-empty, and must contain neither a line terminator nor the row terminator.</param>
    /// <param name="rowTerminator">The resolved row terminator; must not appear in the delimiter.</param>
    /// <param name="encoding">Encoding name; required, non-blank.</param>
    /// <param name="rowTypes">Row types; required, non-empty, unique names, at least one data role, at most one header and one trailer. Several data roles are allowed when each declares a match, all in the same field and with distinct values.</param>
    /// <exception cref="ArgumentException">Any string is blank, the delimiter carries a line terminator or the row terminator, row types are empty, names collide, or the role composition is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rowTypes"/> is null.</exception>
    public DelimitedLayout(
        string version,
        string delimiter,
        char rowTerminator,
        string encoding,
        IReadOnlyList<DelimitedRowDefinition> rowTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);
        ArgumentNullException.ThrowIfNull(rowTypes);

        // An empty delimiter has no boundary to find, so every row would read as a single field.
        if (string.IsNullOrEmpty(delimiter))
        {
            throw new ArgumentException("A layout must declare a non-empty field delimiter.", nameof(delimiter));
        }

        if (delimiter.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException(
                "A line terminator cannot appear in a field delimiter; it would collide with row framing.",
                nameof(delimiter));
        }

        // The row terminator cannot occur inside the delimiter, or framing would end a row part-way through a
        // field boundary and the two would disagree about where the row stops.
        if (delimiter.Contains(rowTerminator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The row terminator must not appear in the field delimiter.", nameof(rowTerminator));
        }

        if (rowTypes.Count == 0)
        {
            throw new ArgumentException("A layout must define at least one row type.", nameof(rowTypes));
        }

        var names = new HashSet<string>(rowTypes.Count, StringComparer.Ordinal);
        var dataRows = new List<DelimitedRowDefinition>(rowTypes.Count);
        DelimitedRowDefinition? header = null;
        DelimitedRowDefinition? trailer = null;

        foreach (var row in rowTypes)
        {
            ArgumentNullException.ThrowIfNull(row);

            if (!names.Add(row.Name))
            {
                throw new ArgumentException($"Duplicate row type name '{row.Name}'.", nameof(rowTypes));
            }

            // Header and trailer are identified by position, so a second of either would leave no way to say
            // which rows belong to which. Data is not: body rows name themselves, so several can coexist.
            var existing = row.Role switch
            {
                RowRole.Header => header,
                RowRole.Trailer => trailer,
                RowRole.Data => null,
                _ => throw new ArgumentException($"Row type '{row.Name}' has an unknown role.", nameof(rowTypes)),
            };

            if (existing is not null)
            {
                throw new ArgumentException(
                    $"Row types '{existing.Name}' and '{row.Name}' both declare role '{row.Role}'; row assignment is positional and would be ambiguous.",
                    nameof(rowTypes));
            }

            switch (row.Role)
            {
                case RowRole.Header: header = row; break;
                case RowRole.Trailer: trailer = row; break;
                default: dataRows.Add(row); break;
            }
        }

        // Without a data row type the layout describes a file with no body — nothing would ever be emitted.
        if (dataRows.Count == 0)
        {
            throw new ArgumentException(
                "A layout must define at least one row type with role 'data'.", nameof(rowTypes));
        }

        DataRows = new ReadOnlyCollection<DelimitedRowDefinition>(dataRows);
        DataMarkerIndex = ResolveDataMarkerIndex(dataRows);

        Version = version;
        Delimiter = delimiter;
        RowTerminator = rowTerminator;
        Encoding = encoding;
        Header = header;
        Trailer = trailer;
        RowTypes = new ReadOnlyCollection<DelimitedRowDefinition>(new List<DelimitedRowDefinition>(rowTypes));
        _byName = RowTypes.ToDictionary(row => row.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves a row type by the name the framing assigned to it, or null if the layout declares no such
    /// type. The delimited counterpart of resolving a fixed-width record by its discriminator value.
    /// </summary>
    /// <param name="name">The row type name; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public DelimitedRowDefinition? ResolveByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    /// <summary>
    /// Resolves which body row type a row is, or null when its marker names no declared type — a file that
    /// does not match its layout, which the caller reports rather than guessing past.
    /// <para>
    /// Splitting lives here because the delimiter does: reading the marker means knowing where fields end,
    /// and that is the layout's own knowledge, not the reader's.
    /// </para>
    /// </summary>
    /// <param name="row">The row's text, without its terminator.</param>
    public DelimitedRowDefinition? ResolveDataRow(ReadOnlySpan<char> row)
    {
        // One body type identified by position: every body row is that type, and no column is read.
        if (DataMarkerIndex is not { } index)
        {
            return DataRows[0];
        }

        if (!DelimitedFields.TryReadAt(row, index, Delimiter, out var marker))
        {
            return null;
        }

        // Scanned rather than hashed: markers are unique by construction, so the answer does not depend on
        // order, and comparing spans keeps a per-row string allocation off the framing path.
        foreach (var candidate in DataRows)
        {
            if (marker.SequenceEqual(candidate.Match!.Value))
            {
                return candidate;
            }
        }

        return null;
    }

    // A single body type needs no marker: position already identifies it. Several do, and all at the same
    // column — markers spread across different columns would mean trying each type in turn, so which type a
    // row resolved to would depend on the order they happened to be declared in.
    private static int? ResolveDataMarkerIndex(List<DelimitedRowDefinition> dataRows)
    {
        if (dataRows.Count == 1 && dataRows[0].Match is null)
        {
            return null;
        }

        int? index = null;
        var markers = new HashSet<string>(dataRows.Count, StringComparer.Ordinal);

        foreach (var row in dataRows)
        {
            var match = row.Match ?? throw new ArgumentException(
                $"Row type '{row.Name}' declares role 'data' without a match; when a layout declares more " +
                "than one data row type each must name itself with a marker.", nameof(dataRows));

            if (index is { } shared && match.Index != shared)
            {
                throw new ArgumentException(
                    $"Row type '{row.Name}' carries its marker at field {match.Index} but '{dataRows[0].Name}' " +
                    $"carries its own at field {shared}; every data row type must be identified by the same field.",
                    nameof(dataRows));
            }

            index ??= match.Index;

            if (!markers.Add(match.Value))
            {
                throw new ArgumentException(
                    $"Two data row types both claim marker '{match.Value}'; a body row would match either.",
                    nameof(dataRows));
            }
        }

        return index;
    }
}
