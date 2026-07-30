namespace Common.Messaging.Contracts.Tests;

public sealed class IngestRecordTests
{
    private static Dictionary<string, FieldValue> SampleFields() => new()
    {
        ["amount"] = new ClearFieldValue(221.73m),
        ["postDate"] = new ClearFieldValue("2022-11-07"),
    };

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var record = new IngestRecord(123401, 148081200, "TRAN", SampleFields());

        Assert.Equal(123401, record.RecordSeq);
        Assert.Equal(148081200, record.ByteOffset);
        Assert.Equal("TRAN", record.RecordType);
        Assert.Equal(2, record.Fields.Count);
        Assert.Equal(new ClearFieldValue(221.73m), record.Fields["amount"]);
    }

    [Fact]
    public void Fields_AreDefensivelyCopied_SoCallerMutationDoesNotLeak()
    {
        var source = SampleFields();
        var record = new IngestRecord(1, 0, "TRAN", source);

        source["injected"] = new ClearFieldValue("x");
        source["amount"] = new ClearFieldValue(999m);

        Assert.Equal(2, record.Fields.Count);
        Assert.False(record.Fields.ContainsKey("injected"));
        Assert.Equal(new ClearFieldValue(221.73m), record.Fields["amount"]);
    }

    [Fact]
    public void Constructor_AllowsEmptyFields()
    {
        var record = new IngestRecord(1, 0, "FILLER", new Dictionary<string, FieldValue>());

        Assert.Empty(record.Fields);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithRecordSeqBelowOne_Throws(long recordSeq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestRecord(recordSeq, 0, "TRAN", SampleFields()));
    }

    [Fact]
    public void Constructor_WithNegativeByteOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestRecord(1, -1, "TRAN", SampleFields()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WithBlankRecordType_Throws(string? recordType)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new IngestRecord(1, 0, recordType!, SampleFields()));
    }

    [Fact]
    public void Constructor_WithNullFields_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new IngestRecord(1, 0, "TRAN", null!));
    }

    [Fact]
    public void Constructor_WithBlankFieldName_Throws()
    {
        var fields = new Dictionary<string, FieldValue> { ["  "] = new ClearFieldValue("x") };

        Assert.Throws<ArgumentException>(() => new IngestRecord(1, 0, "TRAN", fields));
    }

    [Fact]
    public void Constructor_WithNullFieldValue_Throws()
    {
        var fields = new Dictionary<string, FieldValue> { ["k"] = null! };

        Assert.Throws<ArgumentException>(() => new IngestRecord(1, 0, "TRAN", fields));
    }
}
