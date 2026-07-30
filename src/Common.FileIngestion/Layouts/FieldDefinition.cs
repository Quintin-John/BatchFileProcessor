namespace Common.FileIngestion.Layouts;

/// <summary>
/// One field in a fixed-width record: its name, 1-based start position, length, and type.
/// A generic container populated from a soft-coded layout — carries no format-specific knowledge.
/// </summary>
public sealed record FieldDefinition
{
    /// <summary>Field name as declared by the layout.</summary>
    public string Name { get; }

    /// <summary>1-based start position within the record.</summary>
    public int Start { get; }

    /// <summary>Field length in bytes.</summary>
    public int Length { get; }

    /// <summary>How the field's bytes are interpreted.</summary>
    public FieldType Type { get; }

    /// <summary>0-based offset within the record (derived from <see cref="Start"/>).</summary>
    public int Offset => Start - 1;

    /// <summary>1-based inclusive end position (derived from <see cref="Start"/> and <see cref="Length"/>).</summary>
    public int EndInclusive => Start + Length - 1;

    /// <summary>Creates a validated field definition.</summary>
    /// <param name="name">Field name; required, non-blank.</param>
    /// <param name="start">1-based start position; must be at least 1.</param>
    /// <param name="length">Field length; must be at least 1.</param>
    /// <param name="type">Field type.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="length"/> is less than 1.</exception>
    public FieldDefinition(string name, int start, int length, FieldType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        Name = name;
        Start = start;
        Length = length;
        Type = type;
    }
}
