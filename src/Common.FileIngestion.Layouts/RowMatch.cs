namespace Common.FileIngestion.Layouts;

/// <summary>
/// A value one of a row's fields must carry for the row to really be the type its position claims.
/// <para>
/// Positional identification alone is optimistic: the last row of a truncated file looks exactly like a
/// trailer row. A declared match turns that into a verifiable claim, so a short file fails closed instead of
/// silently swallowing its final data row as a control row.
/// </para>
/// The field is addressed by index rather than assumed to be the first, because the column carrying the
/// marker is a property of the feed, not of the engine.
/// </summary>
public sealed record RowMatch
{
    /// <summary>0-based index of the field carrying the marker.</summary>
    public int Index { get; }

    /// <summary>The exact value that field must carry, compared ordinally.</summary>
    public string Value { get; }

    /// <summary>Creates a validated row match.</summary>
    /// <param name="index">0-based field index; must be non-negative.</param>
    /// <param name="value">Expected value; required, non-blank — a blank marker could not distinguish anything.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public RowMatch(int index, string value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Index = index;
        Value = value;
    }
}
