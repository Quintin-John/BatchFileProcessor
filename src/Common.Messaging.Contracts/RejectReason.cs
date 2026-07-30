namespace Common.Messaging.Contracts;

/// <summary>
/// Describes a single field-level validation failure on a rejected record. Format-agnostic:
/// <see cref="Offset"/> and <see cref="Length"/> apply to fixed-length layouts and are null
/// for delimited/index-based layouts.
/// </summary>
public sealed record RejectReason
{
    /// <summary>Name of the field that failed, as declared by the layout.</summary>
    public string Field { get; }

    /// <summary>The validation rule that failed (e.g. <c>decimal</c>, <c>date-format</c>).</summary>
    public string Rule { get; }

    /// <summary>Stable machine-readable reason code (e.g. <c>NON_NUMERIC</c>) for dashboards/metrics.</summary>
    public string Code { get; }

    /// <summary>What the rule expected, if expressible; otherwise null.</summary>
    public string? Expected { get; }

    /// <summary>The offending value or a safe description of it, if available; otherwise null.</summary>
    public string? Actual { get; }

    /// <summary>Byte offset of the field (fixed-length layouts); null when not applicable.</summary>
    public int? Offset { get; }

    /// <summary>Byte length of the field (fixed-length layouts); null when not applicable.</summary>
    public int? Length { get; }

    /// <summary>Creates a validated reject reason.</summary>
    /// <param name="field">Field name; required, non-blank.</param>
    /// <param name="rule">Failed rule; required, non-blank.</param>
    /// <param name="code">Reason code; required, non-blank.</param>
    /// <param name="expected">Optional expected description.</param>
    /// <param name="actual">Optional actual value/description.</param>
    /// <param name="offset">Optional byte offset; must be non-negative when present.</param>
    /// <param name="length">Optional byte length; must be positive when present.</param>
    /// <exception cref="ArgumentException"><paramref name="field"/>, <paramref name="rule"/>, or <paramref name="code"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative or <paramref name="length"/> is less than one.</exception>
    public RejectReason(
        string field,
        string rule,
        string code,
        string? expected = null,
        string? actual = null,
        int? offset = null,
        int? length = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (offset is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be non-negative.");
        }

        if (length is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be positive.");
        }

        Field = field;
        Rule = rule;
        Code = code;
        Expected = expected;
        Actual = actual;
        Offset = offset;
        Length = length;
    }
}
