using System.Text;
using System.Text.Json;

namespace Common.Messaging.Contracts.Tests;

public sealed class IngestRecordJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = MessagingJson.Options;

    private static IngestRecord Record() =>
        new(new RecordLocator(7, 70, "TRAN"),
            new Dictionary<string, FieldValue>
            {
                ["amount"] = new ClearFieldValue(221.73m),
                ["pan"] = new EncryptedFieldValue(new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn")),
            });

    [Fact]
    public void RoundTrip_PreservesLocatorAndFields()
    {
        var json = JsonSerializer.Serialize(Record(), Options);

        var back = JsonSerializer.Deserialize<IngestRecord>(json, Options)!;

        Assert.Equal(7, back.Locator.RecordSeq);
        Assert.Equal(70, back.Locator.ByteOffset);
        Assert.Equal("TRAN", back.Locator.RecordType);
        Assert.Equal(new ClearFieldValue(221.73m), back.Fields["amount"]);
        Assert.IsType<EncryptedFieldValue>(back.Fields["pan"]);
    }

    [Fact]
    public void Write_NoCache_EmitsLocatorAndFieldsShape()
    {
        var json = JsonSerializer.Serialize(Record(), Options);

        Assert.Contains("\"locator\":", json, StringComparison.Ordinal);
        Assert.Contains("\"fields\":", json, StringComparison.Ordinal);
        Assert.Contains("\"recordType\":\"TRAN\"", json, StringComparison.Ordinal);
        Assert.Contains("\"amount\":221.73", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithCachedForm_EmitsThoseBytesVerbatim()
    {
        var record = Record();
        record.SerializedForm = Encoding.UTF8.GetBytes("{\"cached\":true}"); // stand-in wire bytes

        var json = JsonSerializer.Serialize(record, Options);

        Assert.Equal("{\"cached\":true}", json); // reuse path emits the memo raw, bypassing normal serialization
    }

    [Fact]
    public void Write_CachedForm_MatchesUncachedForm_ForTheSameRecord()
    {
        // The cache is only ever the record's own serialized bytes; caching must not change the wire result.
        var uncached = JsonSerializer.SerializeToUtf8Bytes(Record(), Options);
        var record = Record();
        record.SerializedForm = uncached;

        Assert.Equal(uncached, JsonSerializer.SerializeToUtf8Bytes(record, Options));
    }

    [Fact]
    public void Read_NonObject_Throws() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IngestRecord>("123", Options));

    [Fact]
    public void Read_MissingFields_Throws() =>
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<IngestRecord>("{\"locator\":{\"recordSeq\":1,\"byteOffset\":0,\"recordType\":\"T\"}}", Options));
}
