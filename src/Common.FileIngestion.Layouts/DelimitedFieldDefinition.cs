namespace Common.FileIngestion.Layouts;

/// <summary>
/// One field in a delimited row: its name, 0-based position among the row's delimited values, and the same
/// three data-driven flags the fixed-width layout carries — encrypt before publish, required, or skipped
/// (counted for coverage but never emitted). A generic container populated from the soft-coded layout: it
/// carries no interpretation of the value. Where a fixed-width field is addressed by byte start and length,
/// a delimited field is addressed by index; nothing else differs.
/// </summary>
public sealed record DelimitedFieldDefinition
{
    /// <summary>Field name as declared by the layout; travels upstream with the value.</summary>
    public string Name { get; }

    /// <summary>0-based position of this field among the row's delimited values.</summary>
    public int Index { get; }

    /// <summary>Whether this field must be encrypted before publish. Optional in the YAML; absent means clear.</summary>
    public bool Encrypt { get; }

    /// <summary>Whether this field must carry a non-blank value. Optional in the YAML; absent means optional.</summary>
    public bool Required { get; }

    /// <summary>
    /// Whether this field is counted for row coverage but omitted from upstream messages. Optional in the
    /// YAML; absent means emitted. Mutually exclusive with <see cref="Encrypt"/> and <see cref="Required"/> —
    /// a field that is never emitted cannot be encrypted or required.
    /// </summary>
    public bool Skip { get; }

    /// <summary>Creates a validated field definition.</summary>
    /// <param name="name">Field name; required, non-blank.</param>
    /// <param name="index">0-based field position; must be non-negative.</param>
    /// <param name="encrypt">Whether the field must be encrypted before publish; defaults to false (clear).</param>
    /// <param name="required">Whether the field must carry a non-blank value; defaults to false (optional).</param>
    /// <param name="skip">Whether the field is counted for coverage but not emitted; defaults to false (emitted).</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank, or <paramref name="skip"/> is combined with <paramref name="encrypt"/> or <paramref name="required"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public DelimitedFieldDefinition(
        string name, int index, bool encrypt = false, bool required = false, bool skip = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (skip && (encrypt || required))
        {
            throw new ArgumentException("A skipped field cannot be encrypted or required.", nameof(skip));
        }

        Name = name;
        Index = index;
        Encrypt = encrypt;
        Required = required;
        Skip = skip;
    }
}
