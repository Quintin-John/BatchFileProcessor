using System.Collections.ObjectModel;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// A validated delimited layout: the field delimiter, the encoding, and the row types. Construction is
/// fail-closed — a <see cref="DelimitedLayout"/> cannot exist in an invalid state. Generic: the
/// format-specific detail lives entirely in the source data, not here, so a new delimited feed is a new
/// layout file and no code change.
/// <para>
/// Where a fixed-width <see cref="Layout"/> identifies a record by a discriminator at a byte position, a
/// delimited file has no guaranteed discriminator column, so row types are identified positionally: the
/// first <see cref="DelimitedRowDefinition.Rows"/> rows belong to the header type, the last belong to the
/// trailer type, and the data type takes the remainder.
/// </para>
/// </summary>
public sealed class DelimitedLayout
{
    /// <summary>Layout version identifier.</summary>
    public string Version { get; }

    /// <summary>The single character separating fields within a row.</summary>
    public char Delimiter { get; }

    /// <summary>Character encoding name (single-byte).</summary>
    public string Encoding { get; }

    /// <summary>The row types. Defensively copied; read-only.</summary>
    public IReadOnlyList<DelimitedRowDefinition> RowTypes { get; }

    /// <summary>The row type that spans the body of the file. Exactly one always exists.</summary>
    public DelimitedRowDefinition Data { get; }

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
    /// <param name="delimiter">The resolved field delimiter; must not be a line terminator.</param>
    /// <param name="encoding">Encoding name; required, non-blank.</param>
    /// <param name="rowTypes">Row types; required, non-empty, unique names, exactly one data role, at most one header and one trailer.</param>
    /// <exception cref="ArgumentException">Any string is blank, the delimiter is a line terminator, row types are empty, names collide, or the role composition is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rowTypes"/> is null.</exception>
    public DelimitedLayout(
        string version,
        char delimiter,
        string encoding,
        IReadOnlyList<DelimitedRowDefinition> rowTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);
        ArgumentNullException.ThrowIfNull(rowTypes);

        if (delimiter is '\r' or '\n')
        {
            throw new ArgumentException(
                "A line terminator cannot be a field delimiter; it would collide with row framing.", nameof(delimiter));
        }

        if (rowTypes.Count == 0)
        {
            throw new ArgumentException("A layout must define at least one row type.", nameof(rowTypes));
        }

        var names = new HashSet<string>(rowTypes.Count, StringComparer.Ordinal);
        DelimitedRowDefinition? data = null;
        DelimitedRowDefinition? header = null;
        DelimitedRowDefinition? trailer = null;

        foreach (var row in rowTypes)
        {
            ArgumentNullException.ThrowIfNull(row);

            if (!names.Add(row.Name))
            {
                throw new ArgumentException($"Duplicate row type name '{row.Name}'.", nameof(rowTypes));
            }

            // Roles are positional, so a second header (or trailer, or data) would make row assignment
            // ambiguous — there would be no way to say which rows belong to which.
            var existing = row.Role switch
            {
                RowRole.Header => header,
                RowRole.Trailer => trailer,
                RowRole.Data => data,
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
                default: data = row; break;
            }
        }

        // Without a data row type the layout describes a file with no body — nothing would ever be emitted.
        Data = data ?? throw new ArgumentException(
            "A layout must define exactly one row type with role 'data'.", nameof(rowTypes));

        Version = version;
        Delimiter = delimiter;
        Encoding = encoding;
        Header = header;
        Trailer = trailer;
        RowTypes = new ReadOnlyCollection<DelimitedRowDefinition>(new List<DelimitedRowDefinition>(rowTypes));
    }

    /// <summary>
    /// Resolves the row type for a row at a known position, given the file's total row count. Header rows are
    /// counted from the start and trailer rows from the end; everything else is data. Returns null when the
    /// header and trailer together claim more rows than the file holds, which is a malformed file rather than
    /// a programming error — the caller quarantines it instead of faulting.
    /// </summary>
    /// <param name="rowIndex">0-based row position within the file.</param>
    /// <param name="totalRows">Total rows in the file.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rowIndex"/> is negative or not less than <paramref name="totalRows"/>.</exception>
    public DelimitedRowDefinition? ResolveByPosition(long rowIndex, long totalRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rowIndex, totalRows);

        if (HeaderRows + (long)TrailerRows > totalRows)
        {
            return null;
        }

        if (rowIndex < HeaderRows)
        {
            return Header;
        }

        return rowIndex >= totalRows - TrailerRows ? Trailer : Data;
    }
}
