using System.Text.Json;
using Common.FileIngestion.Batching;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Batching;

public sealed class BatcherTests
{
    private static MessageProvenance Provenance() => new("run", "FILE1", "f.dat", "g266", "4.8");

    private static IngestRecord Record(long seq, string value = "x") =>
        new(new RecordLocator(seq, seq * 10, "TRAN"),
            new Dictionary<string, FieldValue> { ["v"] = new ClearFieldValue(value) });

    [Fact]
    public void Add_BelowLimit_ReturnsNull_FlushSeals()
    {
        var batcher = new Batcher(maxRecords: 3, maxContentBytes: 1000, Provenance());

        Assert.Null(batcher.Add(Record(1)));
        Assert.Null(batcher.Add(Record(2)));

        var batch = batcher.Flush();
        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Count);
        Assert.Equal("FILE1-0", batch.MessageId);
        Assert.Equal(0, batch.BatchSeq);
    }

    [Fact]
    public void Add_ReachingMaxRecords_Seals_AndSequenceAdvances()
    {
        var batcher = new Batcher(maxRecords: 2, maxContentBytes: 1000, Provenance());

        Assert.Null(batcher.Add(Record(1)));
        var first = batcher.Add(Record(2));
        Assert.NotNull(first);
        Assert.Equal(2, first!.Count);
        Assert.Equal(0, first.BatchSeq);

        Assert.Null(batcher.Add(Record(3)));
        var second = batcher.Flush();
        Assert.Equal(1, second!.BatchSeq);
        Assert.Equal("FILE1-1", second.MessageId);
    }

    [Fact]
    public void Add_MeasuresTrueSerializedSize_NotCharProxy_Seals()
    {
        // The char-length proxy of this record is ~2 bytes, but its serialized JSON is far larger;
        // a 50-byte cap must trip on the real wire size (M1 poison-batch guard).
        var batcher = new Batcher(maxRecords: 1000, maxContentBytes: 50, Provenance());

        var batch = batcher.Add(Record(1));

        Assert.NotNull(batch);
        Assert.Single(batch!.Records);
    }

    [Fact]
    public void Add_SealsBeforeExceedingByteCap_KeepingBatchUnderLimit()
    {
        var recordSize = JsonSerializer.SerializeToUtf8Bytes(Record(1), MessagingJson.Options).Length;
        var batcher = new Batcher(maxRecords: 1000, maxContentBytes: recordSize * 2 + 1, Provenance());

        Assert.Null(batcher.Add(Record(1)));      // 1 record fits
        Assert.Null(batcher.Add(Record(2)));      // 2 records fit (2*size <= cap)
        var sealed3 = batcher.Add(Record(3));     // 3rd would exceed -> seal prior two, keep the 3rd

        Assert.NotNull(sealed3);
        Assert.Equal(2, sealed3!.Count);          // sealed batch holds only what fit under the cap
        var remaining = batcher.Flush();
        Assert.Equal(3, remaining!.Records[0].Locator.RecordSeq); // record 3 opened the next batch
    }

    [Fact]
    public void Add_ByteCap_MeasuresEscapedValuesExactly_MatchingWireSize()
    {
        // A value with characters the serializer must escape/encode (quotes, '+', '/', '=', non-ASCII). The
        // internal counting measure must equal the real serialized length byte-for-byte, or the seal boundary
        // below would be off — proving the measure matches the wire size including escaping.
        var special = Record(1, "a+b/c=\"éü\"\\x");
        var recordSize = JsonSerializer.SerializeToUtf8Bytes(special, MessagingJson.Options).Length;
        var batcher = new Batcher(maxRecords: 1000, maxContentBytes: recordSize * 2 + 1, Provenance());

        Assert.Null(batcher.Add(Record(1, "a+b/c=\"éü\"\\x")));  // 1 fits
        Assert.Null(batcher.Add(Record(2, "a+b/c=\"éü\"\\x")));  // 2 fit (2*size <= cap)
        var sealed3 = batcher.Add(Record(3, "a+b/c=\"éü\"\\x")); // 3rd exceeds -> seal prior two

        Assert.NotNull(sealed3);
        Assert.Equal(2, sealed3!.Count);
    }

    [Fact]
    public void SealedBatch_RecordsCachedByBatcher_SerializeIdenticallyToUncached()
    {
        // The batcher caches each record's serialized bytes when sizing it; publishing must reuse them and
        // produce a byte-identical batch to one whose records carry no cache (the fallback path).
        var batcher = new Batcher(maxRecords: 3, maxContentBytes: 1_000_000, Provenance());
        batcher.Add(Record(1, "alpha"));
        batcher.Add(Record(2, "beta"));
        var cachedBatch = batcher.Flush()!;

        // An equivalent batch built directly, whose records were never measured (SerializedForm null).
        var uncachedBatch = new IngestBatchMessage(
            cachedBatch.MessageId, Provenance(), cachedBatch.BatchSeq,
            new[] { Record(1, "alpha"), Record(2, "beta") });

        var cachedJson = JsonSerializer.Serialize(cachedBatch, MessagingJson.Options);
        var uncachedJson = JsonSerializer.Serialize(uncachedBatch, MessagingJson.Options);

        Assert.Equal(uncachedJson, cachedJson); // reuse (raw) == fallback, byte-for-byte
        // And it still round-trips back to equivalent records.
        var back = JsonSerializer.Deserialize<IngestBatchMessage>(cachedJson, MessagingJson.Options)!;
        Assert.Equal(2, back.Count);
        Assert.Equal(1, back.Records[0].Locator.RecordSeq);
    }

    [Fact]
    public void Flush_Empty_ReturnsNull()
    {
        Assert.Null(new Batcher(2, 1000, Provenance()).Flush());
    }

    [Fact]
    public void Add_WithFirstBatchSeq_ResumesNumbering()
    {
        var batcher = new Batcher(1, 1000, Provenance(), firstBatchSeq: 4);

        var batch = batcher.Add(Record(1));

        Assert.Equal(4, batch!.BatchSeq);
        Assert.Equal("FILE1-4", batch.MessageId);
    }

    [Fact]
    public void Constructor_NegativeFirstBatchSeq_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Batcher(1, 1000, Provenance(), firstBatchSeq: -1));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(2, 0)]
    public void Constructor_InvalidLimits_Throw(int maxRecords, int maxContentBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Batcher(maxRecords, maxContentBytes, Provenance()));
    }

    [Fact]
    public void Constructor_NullProvenance_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Batcher(2, 1000, null!));
    }

    [Fact]
    public void Add_NullRecord_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Batcher(2, 1000, Provenance()).Add(null!));
    }
}
