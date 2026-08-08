namespace Common.Messaging.Contracts.Tests;

public sealed class IngestRecordTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;

    private const long FixtureSeq = 123401;

    private static RecordLocator Locator() => new(FixtureSeq, FixtureSeq * RecordExtent, RecordExtent, "TRAN");

    private static Dictionary<string, FieldValue> SampleFields() => new()
    {
        ["amount"] = new ClearFieldValue(221.73m),
        ["postDate"] = new ClearFieldValue("2022-11-07"),
    };

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var record = new IngestRecord(Locator(), SampleFields());

        Assert.Equal(Locator(), record.Locator);
        Assert.Equal(2, record.Fields.Count);
        Assert.Equal(new ClearFieldValue(221.73m), record.Fields["amount"]);
    }

    [Fact]
    public void Fields_AreDefensivelyCopied_SoCallerMutationDoesNotLeak()
    {
        var source = SampleFields();
        var record = new IngestRecord(Locator(), source);

        source["injected"] = new ClearFieldValue("x");
        source["amount"] = new ClearFieldValue(999m);

        Assert.Equal(2, record.Fields.Count);
        Assert.False(record.Fields.ContainsKey("injected"));
        Assert.Equal(new ClearFieldValue(221.73m), record.Fields["amount"]);
    }

    [Fact]
    public void Constructor_AllowsEmptyFields()
    {
        var record = new IngestRecord(new RecordLocator(1, 0, RecordExtent, "FILLER"), new Dictionary<string, FieldValue>());

        Assert.Empty(record.Fields);
    }

    [Fact]
    public void Constructor_WithNullLocator_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new IngestRecord(null!, SampleFields()));
    }

    [Fact]
    public void Constructor_WithNullFields_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new IngestRecord(Locator(), null!));
    }

    [Fact]
    public void Constructor_WithBlankFieldName_Throws()
    {
        var fields = new Dictionary<string, FieldValue> { ["  "] = new ClearFieldValue("x") };

        Assert.Throws<ArgumentException>(() => new IngestRecord(Locator(), fields));
    }

    [Fact]
    public void Constructor_WithNullFieldValue_Throws()
    {
        var fields = new Dictionary<string, FieldValue> { ["k"] = null! };

        Assert.Throws<ArgumentException>(() => new IngestRecord(Locator(), fields));
    }
}
