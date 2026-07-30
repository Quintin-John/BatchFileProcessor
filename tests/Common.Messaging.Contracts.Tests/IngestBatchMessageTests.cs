namespace Common.Messaging.Contracts.Tests;

public sealed class IngestBatchMessageTests
{
    private static IngestRecord Record(long seq) =>
        new(seq, seq * 1200, "TRAN", new Dictionary<string, FieldValue> { ["amount"] = new ClearFieldValue(1m) });

    private static IngestBatchMessage Create(IReadOnlyList<IngestRecord>? records = null) =>
        new(
            messageId: "file-abc-1234",
            correlationId: "run-xyz",
            fileId: "file-abc",
            fileName: "g266.dat",
            profile: "g266",
            layoutVersion: "4.8",
            batchSeq: 1234,
            records: records ?? new[] { Record(101), Record(102), Record(103) });

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var message = Create();

        Assert.Equal("file-abc-1234", message.MessageId);
        Assert.Equal("run-xyz", message.CorrelationId);
        Assert.Equal("file-abc", message.FileId);
        Assert.Equal("g266.dat", message.FileName);
        Assert.Equal("g266", message.Profile);
        Assert.Equal("4.8", message.LayoutVersion);
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
    public void Records_AreDefensivelyCopied()
    {
        var source = new List<IngestRecord> { Record(1), Record(2) };
        var message = Create(source);

        source.Add(Record(3));

        Assert.Equal(2, message.Count);
    }

    [Theory]
    [InlineData(null, "c", "f", "n", "p", "v")]
    [InlineData("m", "", "f", "n", "p", "v")]
    [InlineData("m", "c", "  ", "n", "p", "v")]
    [InlineData("m", "c", "f", null, "p", "v")]
    [InlineData("m", "c", "f", "n", "", "v")]
    [InlineData("m", "c", "f", "n", "p", "  ")]
    public void Constructor_WithBlankIdentity_Throws(
        string? messageId, string? correlationId, string? fileId, string? fileName, string? profile, string? layoutVersion)
    {
        Assert.ThrowsAny<ArgumentException>(() => new IngestBatchMessage(
            messageId!, correlationId!, fileId!, fileName!, profile!, layoutVersion!, 0, new[] { Record(1) }));
    }

    [Fact]
    public void Constructor_WithNegativeBatchSeq_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IngestBatchMessage(
            "m", "c", "f", "n", "p", "v", -1, new[] { Record(1) }));
    }

    [Fact]
    public void Constructor_WithNullRecords_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new IngestBatchMessage(
            "m", "c", "f", "n", "p", "v", 0, null!));
    }

    [Fact]
    public void Constructor_WithEmptyRecords_Throws()
    {
        Assert.Throws<ArgumentException>(() => new IngestBatchMessage(
            "m", "c", "f", "n", "p", "v", 0, Array.Empty<IngestRecord>()));
    }

    [Fact]
    public void Constructor_WithNullRecordElement_Throws()
    {
        Assert.Throws<ArgumentException>(() => new IngestBatchMessage(
            "m", "c", "f", "n", "p", "v", 0, new IngestRecord[] { null! }));
    }
}
