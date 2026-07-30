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
    public void Add_ExceedingByteCap_Seals()
    {
        var batcher = new Batcher(maxRecords: 1000, maxContentBytes: 5, Provenance());

        var batch = batcher.Add(Record(1, "long-value")); // "v"(1) + 10 chars > 5

        Assert.NotNull(batch);
        Assert.Single(batch!.Records);
    }

    [Fact]
    public void Flush_Empty_ReturnsNull()
    {
        Assert.Null(new Batcher(2, 1000, Provenance()).Flush());
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
