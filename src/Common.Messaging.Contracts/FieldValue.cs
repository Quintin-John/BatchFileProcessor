namespace Common.Messaging.Contracts;

/// <summary>
/// A single field value carried on a message: either a <see cref="ClearFieldValue"/>
/// (unencrypted) or an <see cref="EncryptedFieldValue"/>. The hierarchy is closed —
/// only these two cases exist — so consumers can exhaustively pattern-match.
/// </summary>
public abstract record FieldValue
{
    // Closed hierarchy: the private-protected constructor means only the two
    // same-assembly subtypes below can derive; external code cannot add cases.
    private protected FieldValue()
    {
    }
}

/// <summary>A field value carried in clear (unencrypted) form.</summary>
public sealed record ClearFieldValue : FieldValue
{
    /// <summary>
    /// The clear scalar value (for example a string, number, boolean, or date). May be
    /// null to represent a present-but-empty field. Its concrete type is defined by the
    /// referenced layout, not by this contract.
    /// </summary>
    public object? Value { get; }

    /// <summary>Creates a clear field value.</summary>
    /// <param name="value">
    /// The clear scalar: null, or a type the wire format can carry — <see cref="string"/>,
    /// <see cref="bool"/>, <see cref="decimal"/>, <see cref="long"/>, <see cref="int"/>,
    /// <see cref="DateOnly"/>, or <see cref="DateTimeOffset"/>. Validated here so an out-of-contract
    /// value fails at construction rather than deep inside serialization.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is a type the wire format cannot carry.</exception>
    public ClearFieldValue(object? value)
    {
        if (value is not (null or string or bool or decimal or long or int or DateOnly or DateTimeOffset))
        {
            throw new ArgumentException(
                $"Clear field value type '{value.GetType()}' is not supported by the wire format; " +
                "use null, string, bool, decimal, long, int, DateOnly, or DateTimeOffset.",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Redacted rendering: the clear value is never emitted to logs or diagnostics. A field the layout marks
    /// <c>encrypt</c> is an <see cref="EncryptedFieldValue"/> by the time it is published, but before that it
    /// is carried here in clear; redacting the rendering means an accidental log/interpolation of the value
    /// cannot leak it. The value itself remains accessible via <see cref="Value"/> for deliberate use.
    /// </summary>
    public override string ToString() => $"{nameof(ClearFieldValue)} {{ Value = {Redacted} }}";

    private const string Redacted = "[redacted]";
}

/// <summary>A field value carried as an encrypted ciphertext envelope.</summary>
public sealed record EncryptedFieldValue : FieldValue
{
    /// <summary>The ciphertext envelope for this field.</summary>
    public EncryptedValue Value { get; }

    /// <summary>Creates an encrypted field value.</summary>
    /// <param name="value">The ciphertext envelope; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public EncryptedFieldValue(EncryptedValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Renders as an encrypted marker — algorithm and key reference only, never ciphertext.</summary>
    public override string ToString() =>
        $"{nameof(EncryptedFieldValue)} {{ {Value.Algorithm}, key={Value.KeyId}/{Value.KeyVersion} }}";
}
