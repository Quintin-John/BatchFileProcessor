using System.Globalization;
using System.Text.Json;

namespace Common.Messaging.Contracts.Tests;

public sealed class SerializationTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;

    private static readonly JsonSerializerOptions Options = MessagingJson.Options;

    private static string Serialize(FieldValue value) => JsonSerializer.Serialize(value, Options);

    private static FieldValue Deserialize(string json) =>
        JsonSerializer.Deserialize<FieldValue>(json, Options)!;

    private static EncryptedValue Envelope() =>
        new("AES-256-GCM", "key-id", "v1", "bm9uY2U=", "Y2lwaGVy", "dGFn");

    // ---- clear field value: write side (every supported scalar type) ----

    [Fact]
    public void Write_ClearNull_EmitsJsonNull() =>
        Assert.Equal("null", Serialize(new ClearFieldValue(null)));

    [Fact]
    public void Write_ClearString_EmitsJsonString() =>
        Assert.Equal("\"abc\"", Serialize(new ClearFieldValue("abc")));

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Write_ClearBool_EmitsJsonBool(bool value, string expected) =>
        Assert.Equal(expected, Serialize(new ClearFieldValue(value)));

    [Fact]
    public void Write_ClearDecimal_EmitsJsonNumber() =>
        Assert.Equal("221.73", Serialize(new ClearFieldValue(221.73m)));

    [Fact]
    public void Write_ClearLong_EmitsJsonNumber() =>
        Assert.Equal("9000000000", Serialize(new ClearFieldValue(9_000_000_000L)));

    [Fact]
    public void Write_ClearInt_EmitsJsonNumber() =>
        Assert.Equal("42", Serialize(new ClearFieldValue(42)));

    [Fact]
    public void Write_ClearDateOnly_EmitsIsoString() =>
        Assert.Equal("\"2022-11-07\"", Serialize(new ClearFieldValue(new DateOnly(2022, 11, 7))));

    [Fact]
    public void Write_ClearDateTimeOffset_EmitsIsoString()
    {
        var dto = new DateTimeOffset(2022, 11, 7, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal($"\"{dto.ToString("O", CultureInfo.InvariantCulture)}\"", Serialize(new ClearFieldValue(dto)));
    }

    // ---- clear field value: read side (canonical CLR types) ----

    [Fact]
    public void Read_JsonString_YieldsClearString() =>
        Assert.Equal(new ClearFieldValue("abc"), Deserialize("\"abc\""));

    [Fact]
    public void Read_JsonNumber_YieldsClearDecimal() =>
        Assert.Equal(new ClearFieldValue(221.73m), Deserialize("221.73"));

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Read_JsonBool_YieldsClearBool(string json, bool expected) =>
        Assert.Equal(new ClearFieldValue(expected), Deserialize(json));

    [Fact]
    public void Read_JsonNull_YieldsClearNull() =>
        Assert.Equal(new ClearFieldValue(null), Deserialize("null"));

    [Fact]
    public void Read_JsonArray_Throws() =>
        Assert.Throws<JsonException>(() => Deserialize("[1,2,3]"));

    // ---- encrypted field value ----

    [Fact]
    public void Write_Encrypted_EmitsEnvelopeObject()
    {
        var json = Serialize(new EncryptedFieldValue(Envelope()));

        Assert.Contains("\"algorithm\":\"AES-256-GCM\"", json, StringComparison.Ordinal);
        Assert.Contains("\"keyId\":\"key-id\"", json, StringComparison.Ordinal);
        Assert.StartsWith("{", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_Encrypted_PreservesEnvelope()
    {
        var original = new EncryptedFieldValue(Envelope());

        var roundTripped = Deserialize(Serialize(original));

        Assert.Equal(original, roundTripped);
    }

    // ---- whole-message round-trips ----

    private static IngestBatchMessage SampleBatch()
    {
        var fields = new Dictionary<string, FieldValue>
        {
            ["amount"] = new ClearFieldValue(221.73m),
            ["postDate"] = new ClearFieldValue("2022-11-07"),
            ["active"] = new ClearFieldValue(true),
            ["memo"] = new ClearFieldValue(null),
            ["pan"] = new EncryptedFieldValue(Envelope()),
        };
        var record = new IngestRecord(new RecordLocator(101, 121200, RecordExtent, "TRAN"), fields);
        var provenance = new MessageProvenance("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");
        return new IngestBatchMessage("file-abc-1234", provenance, 1234, new[] { record });
    }

    [Fact]
    public void RoundTrip_IngestBatchMessage_PreservesEnvelopeAndFields()
    {
        var original = SampleBatch();

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<IngestBatchMessage>(json, Options)!;

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Equal(original.Provenance, result.Provenance);
        Assert.Equal(original.BatchSeq, result.BatchSeq);
        Assert.Equal(original.Count, result.Count);
        Assert.Equal(original.FirstRecordSeq, result.FirstRecordSeq);
        Assert.Equal(original.LastRecordSeq, result.LastRecordSeq);
        Assert.Equal(original.Records[0].Locator, result.Records[0].Locator);

        var originalFields = original.Records[0].Fields;
        var resultFields = result.Records[0].Fields;
        Assert.Equal(originalFields.Count, resultFields.Count);
        Assert.Equal(originalFields["amount"], resultFields["amount"]);
        Assert.Equal(originalFields["postDate"], resultFields["postDate"]);
        Assert.Equal(originalFields["active"], resultFields["active"]);
        Assert.Equal(originalFields["memo"], resultFields["memo"]);
        Assert.Equal(originalFields["pan"], resultFields["pan"]);
    }

    [Fact]
    public void RoundTrip_RejectMessage_PreservesReasonsAndRawRecord()
    {
        var original = new RejectMessage(
            "file-abc-101-reject",
            new MessageProvenance("run-xyz", "file-abc", "source.dat", "feed-a", "1.0"),
            new RecordLocator(101, 121200, RecordExtent, "TRAN"),
            new ClearFieldValue("cmF3"),
            new[]
            {
                new RejectReason("amount", "decimal", "NON_NUMERIC", expected: "numeric", actual: "ABC", offset: 84, length: 17),
                new RejectReason("postDate", "date", "BAD_DATE"),
            });

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<RejectMessage>(json, Options)!;

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Equal(original.Provenance, result.Provenance);
        Assert.Equal(original.Locator, result.Locator);
        Assert.Equal(original.RawRecord, result.RawRecord);
        Assert.Equal(original.Reasons.Count, result.Reasons.Count);
        Assert.Equal(original.Reasons[0], result.Reasons[0]);
        Assert.Equal(original.Reasons[1], result.Reasons[1]);
    }

    // ---- golden shape ----

    [Fact]
    public void Configure_AppliesFieldValueConverter_ToExternalOptions()
    {
        var external = new JsonSerializerOptions();
        MessagingJson.Configure(external);

        var json = JsonSerializer.Serialize<FieldValue>(new ClearFieldValue(221.73m), external);

        Assert.Equal("221.73", json);
    }

    [Fact]
    public void Configure_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MessagingJson.Configure(null!));
    }

    [Fact]
    public void Configure_ProducesWireFormatIdenticalToAuthoritativeOptions()
    {
        // A freshly-configured serializer (the transport path) must match Options byte-for-byte, or the
        // Batcher's size accounting — which measures via Options — diverges from what is actually sent.
        var transport = new JsonSerializerOptions();
        MessagingJson.Configure(transport);
        var sample = new RejectReason("a+b", "rule", "code"); // nullable fields null; '+' inside a value

        var viaTransport = JsonSerializer.Serialize(sample, transport);

        Assert.Equal(JsonSerializer.Serialize(sample, MessagingJson.Options), viaTransport);
        Assert.DoesNotContain("expected", viaTransport, StringComparison.Ordinal);        // WhenWritingNull omits nulls
        Assert.Contains("a+b", viaTransport, StringComparison.Ordinal);                   // relaxed encoder keeps '+'
        Assert.DoesNotContain("002B", viaTransport, StringComparison.OrdinalIgnoreCase);  // not escaped to +
    }

    [Fact]
    public void GoldenShape_UsesCamelCase_NestedProvenanceAndLocator_ClearScalars_EncryptedObject()
    {
        var json = JsonSerializer.Serialize(SampleBatch(), Options);

        // camelCase identity + derived fields present at the top level
        Assert.Contains("\"messageId\":\"file-abc-1234\"", json, StringComparison.Ordinal);
        Assert.Contains("\"count\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"firstRecordSeq\":101", json, StringComparison.Ordinal);
        // nested provenance
        Assert.Contains("\"provenance\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"fileId\":\"file-abc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"layoutVersion\":\"1.0\"", json, StringComparison.Ordinal);
        // nested locator
        Assert.Contains("\"locator\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"recordSeq\":101", json, StringComparison.Ordinal);
        Assert.Contains("\"recordType\":\"TRAN\"", json, StringComparison.Ordinal);
        // clear scalar is bare; encrypted is a nested object
        Assert.Contains("\"amount\":221.73", json, StringComparison.Ordinal);
        Assert.Contains("\"active\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"pan\":{", json, StringComparison.Ordinal);
        // no PascalCase leakage
        Assert.DoesNotContain("\"MessageId\"", json, StringComparison.Ordinal);
    }
}
