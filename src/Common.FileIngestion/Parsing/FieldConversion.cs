using Common.Messaging.Contracts;

namespace Common.FileIngestion.Parsing;

/// <summary>
/// The outcome of converting one field: either a typed <see cref="Value"/> or a
/// <see cref="Reason"/> explaining why the value was rejected. Exactly one is set.
/// </summary>
public sealed class FieldConversion
{
    private FieldConversion(FieldValue? value, RejectReason? reason)
    {
        Value = value;
        Reason = reason;
    }

    /// <summary>The converted value when successful; otherwise null.</summary>
    public FieldValue? Value { get; }

    /// <summary>The rejection reason when unsuccessful; otherwise null.</summary>
    public RejectReason? Reason { get; }

    /// <summary>True when the field converted successfully.</summary>
    public bool IsSuccess => Reason is null;

    /// <summary>Creates a successful conversion.</summary>
    /// <param name="value">The converted value; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static FieldConversion Success(FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new FieldConversion(value, null);
    }

    /// <summary>Creates a rejected conversion.</summary>
    /// <param name="reason">The rejection reason; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is null.</exception>
    public static FieldConversion Rejected(RejectReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new FieldConversion(null, reason);
    }
}
