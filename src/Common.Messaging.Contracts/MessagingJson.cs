using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Messaging.Contracts.Serialization;

namespace Common.Messaging.Contracts;

/// <summary>
/// The single, authoritative source of the messaging wire format. Producers and consumers must either
/// serialize/deserialize through <see cref="Options"/> or configure their own serializer through
/// <see cref="Configure"/>, so the shape stays identical on every path. Naming is camelCase; nulls are
/// omitted; escaping is relaxed (message-bus payload, not HTML); the <see cref="FieldValue"/> shape is
/// defined by its converter.
/// </summary>
public static class MessagingJson
{
    /// <summary>
    /// Shared, read-only serializer options that define the contract's wire format.
    /// The instance is frozen and safe to reuse across threads.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Applies the contract's complete wire format to <paramref name="options"/>: camelCase naming,
    /// null omission, relaxed (non-HTML) escaping, and the <see cref="FieldValue"/> converter. This is
    /// the one definition of the format — <see cref="Options"/> and every external/transport serializer
    /// configure through it, so no caller can partially match the shape and silently drift.
    /// </summary>
    /// <param name="options">The serializer options to configure; must not already be read-only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // Message-bus payload (not HTML): relaxed escaping keeps values like the '+' in an ISO date
        // offset or a base64 field literal as '+' rather than emitting +.
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.Converters.Add(new FieldValueJsonConverter());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
