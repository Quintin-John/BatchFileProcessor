using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Common.Messaging.Contracts.Serialization;

/// <summary>
/// Serializes <see cref="FieldValue"/> using the wire shape defined by the contract:
/// a <see cref="ClearFieldValue"/> is written as a bare JSON scalar, and an
/// <see cref="EncryptedFieldValue"/> as the <see cref="EncryptedValue"/> object.
/// On read, a JSON object is an encrypted value and any scalar is a clear value —
/// clear values are always scalars, so the structural discriminator is unambiguous.
/// </summary>
internal sealed class FieldValueJsonConverter : JsonConverter<FieldValue>
{
    // A JSON null is a clear field carrying null, not an absent value — so the converter
    // must run for null tokens rather than letting the serializer short-circuit to a null reference.
    public override bool HandleNull => true;

    public override FieldValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var envelope = JsonSerializer.Deserialize<EncryptedValue>(ref reader, options)
                ?? throw new JsonException("Encrypted field value envelope was null.");
            return new EncryptedFieldValue(envelope);
        }

        return new ClearFieldValue(ReadClearScalar(ref reader));
    }

    public override void Write(Utf8JsonWriter writer, FieldValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        switch (value)
        {
            case EncryptedFieldValue encrypted:
                JsonSerializer.Serialize(writer, encrypted.Value, options);
                break;
            case ClearFieldValue clear:
                WriteClearScalar(writer, clear.Value);
                break;
            default:
                throw new JsonException($"Unsupported FieldValue type '{value.GetType()}'.");
        }
    }

    private static object? ReadClearScalar(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unsupported clear field token '{reader.TokenType}'."),
        };

    private static void WriteClearScalar(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case DateOnly date:
                writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            default:
                throw new JsonException($"Unsupported clear field value type '{value.GetType()}'.");
        }
    }
}
