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
/// <param name="Value">
/// The clear scalar value (for example a string, number, boolean, or date). May be
/// null to represent a present-but-empty field. Its concrete type is defined by the
/// referenced layout, not by this contract.
/// </param>
public sealed record ClearFieldValue(object? Value) : FieldValue;

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
}
