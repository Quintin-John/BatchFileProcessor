namespace Common.FileIngestion.Layouts;

/// <summary>
/// The single definition of what a field boundary is in a delimited row.
/// <para>
/// Splitting happens on both sides of the reader/parser seam — the reader reads one field to verify a row's
/// declared marker, the parser reads every field to map the row — and those are the same rule, so they are
/// one implementation. Two copies could disagree without any compile error, and the first change to make
/// them disagree would be the one that matters: the reader would then locate a marker in a different column
/// than the parser reads a value from.
/// </para>
/// Allocation-free: values are returned as spans, so a caller materialises only the fields it keeps.
/// </summary>
public ref struct DelimitedFields
{
    private readonly char _delimiter;
    private ReadOnlySpan<char> _remaining;
    private bool _hasMore;

    /// <summary>Begins reading the fields of one row.</summary>
    /// <param name="row">The row's text, without its terminator.</param>
    /// <param name="delimiter">The delimiter the layout declares.</param>
    public DelimitedFields(ReadOnlySpan<char> row, char delimiter)
    {
        _delimiter = delimiter;
        _remaining = row;
        _hasMore = true;
    }

    /// <summary>
    /// Reads the next field, or returns false once the row is exhausted. An empty row yields exactly one
    /// empty field, and a trailing delimiter yields a final empty field — both are values the row carries,
    /// not absences, and the field count has to see them.
    /// </summary>
    /// <param name="value">The field's text; empty when the field is empty.</param>
    public bool TryReadNext(out ReadOnlySpan<char> value)
    {
        if (!_hasMore)
        {
            value = default;
            return false;
        }

        var separator = _remaining.IndexOf(_delimiter);
        if (separator < 0)
        {
            value = _remaining;
            _remaining = default;
            _hasMore = false;
            return true;
        }

        value = _remaining[..separator];
        _remaining = _remaining[(separator + 1)..];
        return true;
    }

    /// <summary>
    /// Counts the fields a row carries, without materialising any of them — so a row whose count is wrong
    /// costs nothing to reject.
    /// </summary>
    /// <param name="row">The row's text, without its terminator.</param>
    /// <param name="delimiter">The delimiter the layout declares.</param>
    public static int Count(ReadOnlySpan<char> row, char delimiter)
    {
        var count = 1;
        foreach (var character in row)
        {
            if (character == delimiter)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Reads the field at a 0-based position, or returns false when the row carries fewer fields than that.
    /// </summary>
    /// <param name="row">The row's text, without its terminator.</param>
    /// <param name="index">0-based field position; must be non-negative.</param>
    /// <param name="delimiter">The delimiter the layout declares.</param>
    /// <param name="value">The field's text when present.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static bool TryReadAt(ReadOnlySpan<char> row, int index, char delimiter, out ReadOnlySpan<char> value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        // Walk to the requested position; the final successful read leaves that field in `value`.
        var fields = new DelimitedFields(row, delimiter);
        value = default;
        for (var position = 0; position <= index; position++)
        {
            if (!fields.TryReadNext(out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }
}
