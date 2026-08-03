using System.Text.Json;
using System.Text.Json.Serialization;

namespace Common.Messaging.Contracts.Serialization;

/// <summary>
/// Serializes <see cref="IngestRecord"/> as <c>{ "locator": …, "fields": … }</c> — the same shape the
/// default serializer produced — but reuses a record's cached wire bytes when present, so a record measured
/// for the batch byte-cap is not serialized a second time at publish. When <see cref="IngestRecord.SerializedForm"/>
/// is set the bytes are emitted verbatim; otherwise the record is written normally, delegating the locator and
/// the (layout-driven, dynamic) field map to the standard serializer so the wire shape is identical either way.
/// </summary>
internal sealed class IngestRecordJsonConverter : JsonConverter<IngestRecord>
{
    private const string LocatorProperty = "locator";
    private const string FieldsProperty = "fields";

    public override IngestRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object for {nameof(IngestRecord)}, found '{reader.TokenType}'.");
        }

        RecordLocator? locator = null;
        IReadOnlyDictionary<string, FieldValue>? fields = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected a property name, found '{reader.TokenType}'.");
            }

            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case LocatorProperty:
                    locator = JsonSerializer.Deserialize<RecordLocator>(ref reader, options);
                    break;
                case FieldsProperty:
                    fields = JsonSerializer.Deserialize<Dictionary<string, FieldValue>>(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (locator is null || fields is null)
        {
            throw new JsonException($"{nameof(IngestRecord)} requires both '{LocatorProperty}' and '{FieldsProperty}'.");
        }

        return new IngestRecord(locator, fields);
    }

    public override void Write(Utf8JsonWriter writer, IngestRecord value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        // Reuse the bytes produced when the record was sized for the byte cap, so it is serialized only once.
        if (value.SerializedForm is { } cached)
        {
            writer.WriteRawValue(cached.Span);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(LocatorProperty);
        JsonSerializer.Serialize(writer, value.Locator, options);
        writer.WritePropertyName(FieldsProperty);
        JsonSerializer.Serialize(writer, value.Fields, options);
        writer.WriteEndObject();
    }
}
