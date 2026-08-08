using System.Collections.ObjectModel;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// One row type in a delimited layout: what it is (<see cref="Role"/>), how many rows it spans, its own
/// ordered fields, and whether it is consumed for framing but never emitted (<see cref="Skip"/>). The
/// delimited counterpart of a fixed-width record type — a header or trailer has a different shape from a
/// data row, so it declares its own fields rather than being discarded sight unseen. A skipped row type
/// needs no fields and is exempt from index-coverage validation, exactly as a skipped record type is exempt
/// from byte coverage.
/// </summary>
public sealed class DelimitedRowDefinition
{
    /// <summary>Logical name of the row type (the layout key).</summary>
    public string Name { get; }

    /// <summary>What this row type is, and therefore how rows are assigned to it.</summary>
    public RowRole Role { get; }

    /// <summary>
    /// How many rows this type spans. Positive for <see cref="RowRole.Header"/> and
    /// <see cref="RowRole.Trailer"/>, which are identified by position. Always 0 for
    /// <see cref="RowRole.Data"/>, which spans every row the header and trailer do not claim and therefore
    /// cannot state a count up front.
    /// </summary>
    public int Rows { get; }

    /// <summary>
    /// Whether this row type is consumed for framing but never emitted upstream. Layout-driven: a trailer
    /// carrying a control total can be declared with fields and emitted, or skipped, without a code change.
    /// </summary>
    public bool Skip { get; }

    /// <summary>Ordered field definitions. Defensively copied; read-only; empty only for a skipped row type.</summary>
    public IReadOnlyList<DelimitedFieldDefinition> Fields { get; }

    /// <summary>
    /// An optional marker naming which rows are this type, or null when position alone is trusted.
    /// <para>
    /// It plays a different part per role, and both are framing rather than business validation. On a header
    /// or trailer, which position already identifies, it verifies that claim. On a data row type it is the
    /// identification: several body row types can coexist, each named by its own marker, exactly as a
    /// fixed-width layout distinguishes record types by discriminator.
    /// </para>
    /// </summary>
    public RowMatch? Match { get; }

    /// <summary>Creates a validated row definition.</summary>
    /// <param name="name">Row-type name; required, non-blank.</param>
    /// <param name="role">What the row type is.</param>
    /// <param name="rows">Rows spanned; at least 1 for header/trailer, exactly 0 for data.</param>
    /// <param name="fields">Ordered fields; required, no null elements, non-empty unless <paramref name="skip"/>. Copied defensively. Indexes must cover 0..n-1 with no gap, overlap, or duplicate name.</param>
    /// <param name="skip">Whether the row type is consumed but not emitted; defaults to false.</param>
    /// <param name="match">Optional marker: verification on a header or trailer, identification on a data row type. Must fall within the declared fields when the type declares any.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank, <paramref name="fields"/> is empty when not skipped or contains a null, indexes do not cover 0..n-1, a field name repeats, or <paramref name="match"/> falls outside the declared fields.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> disagrees with <paramref name="role"/>, or <paramref name="role"/> is not a declared value.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public DelimitedRowDefinition(
        string name,
        RowRole role,
        int rows,
        IReadOnlyList<DelimitedFieldDefinition> fields,
        bool skip = false,
        RowMatch? match = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(fields);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown row role.");
        }

        // A positional row type must say how many rows it claims; data claims whatever is left, so a count
        // there would be a second source of truth that could contradict the file.
        if (role == RowRole.Data)
        {
            if (rows != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rows), rows, "A data row type spans the remainder of the file and must not declare a row count.");
            }
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        }

        if (!skip && fields.Count == 0)
        {
            throw new ArgumentException("A row type must define at least one field unless it is skipped.", nameof(fields));
        }

        var copy = new List<DelimitedFieldDefinition>(fields.Count);
        foreach (var field in fields)
        {
            if (field is null)
            {
                throw new ArgumentException("Fields must not contain null elements.", nameof(fields));
            }

            copy.Add(field);
        }

        ValidateContiguousIndexes(name, copy);

        // A row type that maps its own columns must reach far enough to include its marker, or it declares a
        // marker no row of that type could ever be checked against. A skipped type maps nothing, so the
        // column carrying its marker is simply one it does not name — the physical row still has it.
        if (match is not null && copy.Count > 0 && match.Index >= copy.Count)
        {
            throw new ArgumentException(
                $"Row type '{name}': match is at field {match.Index} but the type declares only {copy.Count} field(s).",
                nameof(match));
        }

        Name = name;
        Role = role;
        Rows = rows;
        Skip = skip;
        Match = match;
        Fields = new ReadOnlyCollection<DelimitedFieldDefinition>(copy);
    }

    // Fields must cover 0..n-1 exactly once, in declaration order, with distinct names. This is the delimited
    // equivalent of a fixed-width record tiling its bytes: every value the row carries is accounted for, so a
    // silently unmapped column cannot slip past. Value correctness is not policed — only completeness.
    private static void ValidateContiguousIndexes(string name, List<DelimitedFieldDefinition> fields)
    {
        var seen = new HashSet<string>(fields.Count, StringComparer.Ordinal);
        for (var expected = 0; expected < fields.Count; expected++)
        {
            var field = fields[expected];
            if (field.Index != expected)
            {
                throw new ArgumentException(
                    $"Row type '{name}': field '{field.Name}' has index {field.Index}, expected {expected} (gap, overlap, or out of order).",
                    nameof(fields));
            }

            if (!seen.Add(field.Name))
            {
                throw new ArgumentException($"Row type '{name}': duplicate field name '{field.Name}'.", nameof(fields));
            }
        }
    }
}
