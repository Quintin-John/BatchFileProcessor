namespace Common.Messaging.Contracts.Tests;

public sealed class IngestBatchMessageTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;

    private static MessageProvenance Provenance() => new("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");

    private static IngestRecord Record(long seq) =>
        new(new RecordLocator(seq, seq * RecordExtent, RecordExtent, "TRAN"),
            new Dictionary<string, FieldValue> { ["amount"] = new ClearFieldValue(1m) });

    private static IngestRecord RecordAt(long seq, long offset, int extent) =>
        new(new RecordLocator(seq, offset, extent, "ROW"),
            new Dictionary<string, FieldValue> { ["a"] = new ClearFieldValue("x") });

    private static IngestBatchMessage Create(IReadOnlyList<IngestRecord>? records = null) =>
        new("file-abc-1234", Provenance(), 1234, records ?? new[] { Record(101), Record(102), Record(103) });

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var message = Create();

        Assert.Equal("file-abc-1234", message.MessageId);
        Assert.Equal(Provenance(), message.Provenance);
        Assert.Equal(1234, message.BatchSeq);
    }

    [Fact]
    public void Count_IsDerivedFromRecords()
    {
        var message = Create();

        Assert.Equal(3, message.Count);
        Assert.Equal(message.Records.Count, message.Count);
    }

    [Fact]
    public void FirstAndLastRecordSeq_AreMinAndMax_RegardlessOfOrder()
    {
        var message = Create(new[] { Record(103), Record(101), Record(102) });

        Assert.Equal(101, message.FirstRecordSeq);
        Assert.Equal(103, message.LastRecordSeq);
    }

    [Fact]
    public void EndByteOffset_IsMax_RegardlessOfOrder()
    {
        // Records supplied out of offset order; EndByteOffset must be the furthest record's end, not the
        // last element's.
        var furthest = Record(103);
        var message = Create(new[] { furthest, Record(101), Record(102) });

        Assert.Equal(furthest.Locator.EndByteOffset, message.EndByteOffset);
    }

    [Fact]
    public void EndByteOffset_WithVariableLengthRecords_UsesMaxExtentNotMaxOffset()
    {
        // The delimited case: the furthest-reaching record is not necessarily the highest-offset one when
        // extents differ. A resume point taken from offset alone would land inside the last record.
        var longFirst = RecordAt(1, offset: 0, extent: 90);
        var shortLast = RecordAt(2, offset: 10, extent: 5);

        var message = new IngestBatchMessage("m", Provenance(), 0, new[] { longFirst, shortLast });

        Assert.True(shortLast.Locator.ByteOffset > longFirst.Locator.ByteOffset); // higher offset…
        Assert.Equal(longFirst.Locator.EndByteOffset, message.EndByteOffset);     // …but not the furthest end
    }

    [Fact]
    public void EndByteOffset_WithSingleRecord_IsThatRecordsEnd()
    {
        var only = Record(7);
        var message = Create(new[] { only });

        Assert.Equal(only.Locator.EndByteOffset, message.EndByteOffset);
    }

    [Fact]
    public void Records_AreDefensivelyCopied()
    {
        var source = new List<IngestRecord> { Record(1), Record(2) };
        var message = Create(source);

        source.Add(Record(3));

        Assert.Equal(2, message.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WithBlankMessageId_Throws(string? messageId)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new IngestBatchMessage(messageId!, Provenance(), 0, new[] { Record(1) }));
    }

    [Fact]
    public void Constructor_WithNullProvenance_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new IngestBatchMessage("m", null!, 0, new[] { Record(1) }));
    }

    [Fact]
    public void Constructor_WithNegativeBatchSeq_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IngestBatchMessage("m", Provenance(), -1, new[] { Record(1) }));
    }

    [Fact]
    public void Constructor_WithNullRecords_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new IngestBatchMessage("m", Provenance(), 0, null!));
    }

    [Fact]
    public void Constructor_WithEmptyRecords_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new IngestBatchMessage("m", Provenance(), 0, Array.Empty<IngestRecord>()));
    }

    [Fact]
    public void Constructor_WithNullRecordElement_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new IngestBatchMessage("m", Provenance(), 0, new IngestRecord[] { null! }));
    }
}
