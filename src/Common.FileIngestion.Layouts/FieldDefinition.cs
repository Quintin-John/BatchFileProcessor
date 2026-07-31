namespace Common.FileIngestion.Layouts;

/// <summary>
/// One field in a record: its name, 1-based start position, length, and three optional, data-driven
/// flags — whether it must be encrypted before publish, whether a value is required, and whether it is
/// skipped (tiled for coverage but never emitted upstream). A generic container populated from the
/// soft-coded layout: it carries no interpretation of the value; the pump slices the bytes at this
/// position, optionally encrypts them, and rejects the record if a required field is blank — nothing more.
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

    /// <summary>
    /// Whether this field is tiled for record coverage but omitted from upstream messages (e.g. FILLER or
    /// padding). Optional in the YAML; absent means emitted. Mutually exclusive with <see cref="Encrypt"/>
    /// and <see cref="Required"/> — a field that is never emitted cannot be encrypted or required.
    /// </summary>
    public bool Skip { get; }

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
    /// <param name="skip">Whether the field is tiled for coverage but not emitted upstream; defaults to false (emitted).</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank, or <paramref name="skip"/> is combined with <paramref name="encrypt"/> or <paramref name="required"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="length"/> is less than 1.</exception>
    public FieldDefinition(string name, int start, int length, bool encrypt = false, bool required = false, bool skip = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        if (skip && (encrypt || required))
        {
            throw new ArgumentException("A skipped field cannot be encrypted or required.", nameof(skip));
        }

        Name = name;
        Start = start;
        Length = length;
        Encrypt = encrypt;
        Required = required;
        Skip = skip;
    }
}
