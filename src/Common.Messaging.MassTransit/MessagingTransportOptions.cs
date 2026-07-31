namespace Common.Messaging.MassTransit;

/// <summary>Supported message transports. Every member must be wired in the composition switch —
/// a value that validates but is not actually registered is a fail-closed gap, so unimplemented
/// transports are not declared here until they are supported.</summary>
public enum MessagingTransport
{
    /// <summary>RabbitMQ (AMQP).</summary>
    RabbitMq,
}

/// <summary>
/// Soft-coded transport configuration. Bound from application config; nothing hardcoded.
/// </summary>
public sealed class MessagingTransportOptions
{
    /// <summary>Which transport to use.</summary>
    public MessagingTransport Transport { get; init; }

    /// <summary>Connection string / URI for the transport (AMQP URI for RabbitMQ).</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Optional prefix applied to endpoint names for consistent naming across services.</summary>
    public string? EndpointPrefix { get; init; }

    /// <summary>Validates the options. Fail-closed on invalid configuration.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Transport"/> is not a defined value.</exception>
    /// <exception cref="ArgumentException"><see cref="ConnectionString"/> is blank.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(Transport))
        {
            throw new InvalidOperationException($"Unknown transport '{(int)Transport}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionString);
    }
}
