namespace Common.FileIngestion.Layouts;

/// <summary>
/// One field in a record: its name, 1-based start position, length, and two optional, data-driven
/// flags — whether it must be encrypted before publish, and whether a value is required. A generic
/// container populated from the soft-coded layout: it carries no interpretation of the value; the pump
/// slices the bytes at this position, optionally encrypts them, and rejects the record if a required
/// field is blank — nothing more.
/// </summary>
public sealed record FieldDefinition
{
    /// <summary>Field name as declared by the layout; travels upstream with the value.</summary>
    public string Name { get; }

    /// <summary>1-based start position within the record.</summary>
    public int Start { get; }

    /// <summary>Field length.</summary>
    public int Length { get; }

    /// <summary>Whether this field must be encrypted before publish. Optional in the YAML; absent means clear.</summary>
    public bool Encrypt { get; }

    /// <summary>Whether this field must carry a non-blank value. Optional in the YAML; absent means optional.</summary>
    public bool Required { get; }

    /// <summary>0-based offset within the record (derived from <see cref="Start"/>).</summary>
    public int Offset => Start - 1;

    /// <summary>1-based inclusive end position (derived from <see cref="Start"/> and <see cref="Length"/>).</summary>
    public int EndInclusive => Start + Length - 1;

    /// <summary>Creates a validated field definition.</summary>
    /// <param name="name">Field name; required, non-blank.</param>
    /// <param name="start">1-based start position; must be at least 1.</param>
    /// <param name="length">Field length; must be at least 1.</param>
    /// <param name="encrypt">Whether the field must be encrypted before publish; defaults to false (clear).</param>
    /// <param name="required">Whether the field must carry a non-blank value; defaults to false (optional).</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="length"/> is less than 1.</exception>
    public FieldDefinition(string name, int start, int length, bool encrypt = false, bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        Name = name;
        Start = start;
        Length = length;
        Encrypt = encrypt;
        Required = required;
    }
}
