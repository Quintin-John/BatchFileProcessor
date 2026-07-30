using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Messaging.Contracts.Serialization;

namespace Common.Messaging.Contracts;

/// <summary>
/// The single, authoritative source of the messaging wire format. Producers and consumers
/// must serialize/deserialize contract types through <see cref="Options"/> so the shape
/// stays identical on both sides. Naming is camelCase; the <see cref="FieldValue"/> shape
/// is defined by its converter.
/// </summary>
public static class MessagingJson
{
    /// <summary>
    /// Shared, read-only serializer options that define the contract's wire format.
    /// The instance is frozen and safe to reuse across threads.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Message-bus payload (not HTML): relaxed escaping keeps values like the '+'
            // in an ISO date offset literal rather than +.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new FieldValueJsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
