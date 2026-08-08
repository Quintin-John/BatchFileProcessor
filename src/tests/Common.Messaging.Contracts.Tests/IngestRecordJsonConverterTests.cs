using System.Text;
using System.Text.Json;

namespace Common.Messaging.Contracts.Tests;

public sealed class IngestRecordJsonConverterTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 10;

    private static readonly JsonSerializerOptions Options = MessagingJson.Options;

    private static IngestRecord Record() =>
        new(new RecordLocator(7, 70, RecordExtent, "TRAN"),
            new Dictionary<string, FieldValue>
            {
                ["plain"] = new ClearFieldValue(221.73m),
                ["encrypted"] = new EncryptedFieldValue(new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn")),
            });

    [Fact]
    public void RoundTrip_PreservesLocatorAndFields()
    {
        var json = JsonSerializer.Serialize(Record(), Options);

        var back = JsonSerializer.Deserialize<IngestRecord>(json, Options)!;

        Assert.Equal(7, back.Locator.RecordSeq);
        Assert.Equal(70, back.Locator.ByteOffset);
        Assert.Equal("TRAN", back.Locator.RecordType);
        Assert.Equal(new ClearFieldValue(221.73m), back.Fields["plain"]);
        Assert.IsType<EncryptedFieldValue>(back.Fields["encrypted"]);
    }

    [Fact]
    public void Write_NoCache_EmitsExactWireShape()
    {
        // Locks the exact wire shape the converter's fallback must reproduce (the shape consumers already
        // receive): {locator:{recordSeq,byteOffset,byteLength,recordType}, fields:{...}}, camelCase, in this
        // order, with no extra properties. A drift here would break consumers and be invisible to
        // cached-vs-uncached parity tests (which both use this converter), so it is pinned as an explicit
        // string. Note the absence of endByteOffset: it is derived and deliberately kept off the wire.
        var record = new IngestRecord(
            new RecordLocator(7, 70, RecordExtent, "TRAN"),
            new Dictionary<string, FieldValue> { ["plain"] = new ClearFieldValue(221.73m) });

        var json = JsonSerializer.Serialize(record, Options);

        Assert.Equal(
            "{\"locator\":{\"recordSeq\":7,\"byteOffset\":70,\"byteLength\":10,\"recordType\":\"TRAN\"}," +
            "\"fields\":{\"plain\":221.73}}",
            json);
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
            () => JsonSerializer.Deserialize<IngestRecord>(
                "{\"locator\":{\"recordSeq\":1,\"byteOffset\":0,\"byteLength\":1,\"recordType\":\"T\"}}", Options));

    [Fact]
    public void Read_LocatorMissingByteLength_FailsClosed()
    {
        // An older-shaped payload carries no extent. Defaulting it to 0 would produce a locator whose
        // EndByteOffset equals its ByteOffset, silently stalling any resume derived from it — so reject it.
        Assert.ThrowsAny<Exception>(
            () => JsonSerializer.Deserialize<IngestRecord>(
                "{\"locator\":{\"recordSeq\":1,\"byteOffset\":0,\"recordType\":\"T\"},\"fields\":{}}", Options));
    }

    [Fact]
    public void RoundTrip_PreservesByteLength()
    {
        var json = JsonSerializer.Serialize(Record(), Options);

        var back = JsonSerializer.Deserialize<IngestRecord>(json, Options)!;

        Assert.Equal(RecordExtent, back.Locator.ByteLength);
        Assert.Equal(Record().Locator.EndByteOffset, back.Locator.EndByteOffset);
    }
}
