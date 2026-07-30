using System.Text.Json;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Configures a transport's JSON serializer to match the messaging contract's wire format:
/// camelCase names and the contract's <see cref="FieldValue"/> converter. Applied to MassTransit's
/// serializer options so messages serialize identically to <see cref="MessagingJson"/>.
/// </summary>
public static class MessagingSerialization
{
    /// <summary>Applies the contract's naming and converters to the given serializer options.</summary>
    /// <param name="options">The transport serializer options to configure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        MessagingJson.RegisterConverters(options);
    }
}
