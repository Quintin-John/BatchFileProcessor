namespace Common.Messaging.Contracts.Tests;

public sealed class RejectMessageTests
{
    private static MessageProvenance Provenance() => new("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");

    // Bytes one fixture record occupies, terminator included; the offset is derived from it, not written out.
    private const int RecordExtent = 1200;
    private const long FixtureSeq = 101;

    private static RecordLocator Locator() => new(FixtureSeq, FixtureSeq * RecordExtent, RecordExtent, "TRAN");

    private static RejectReason Reason() => new("amount", "decimal", "NON_NUMERIC");

    private static RejectMessage Create(IReadOnlyList<RejectReason>? reasons = null) =>
        new("file-abc-101-reject", Provenance(), Locator(), new ClearFieldValue("cmF3LXJlY29yZA=="),
            reasons ?? new[] { Reason() });

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var message = Create();

        Assert.Equal("file-abc-101-reject", message.MessageId);
        Assert.Equal(Provenance(), message.Provenance);
        Assert.Equal(Locator(), message.Locator);
        Assert.Equal(new ClearFieldValue("cmF3LXJlY29yZA=="), message.RawRecord);
        Assert.Single(message.Reasons);
    }

    [Fact]
    public void RawRecord_AcceptsEncryptedContent()
    {
        var encrypted = new EncryptedFieldValue(
            new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        var message = new RejectMessage("m", Provenance(), Locator(), encrypted, new[] { Reason() });

        Assert.IsType<EncryptedFieldValue>(message.RawRecord);
    }

    [Fact]
    public void Reasons_AreDefensivelyCopied()
    {
        var source = new List<RejectReason> { Reason() };
        var message = Create(source);

        source.Add(new RejectReason("postDate", "date", "BAD_DATE"));

        Assert.Single(message.Reasons);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WithBlankMessageId_Throws(string? messageId)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new RejectMessage(messageId!, Provenance(), Locator(), new ClearFieldValue("x"), new[] { Reason() }));
    }

    [Fact]
    public void Constructor_WithNullProvenance_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RejectMessage("m", null!, Locator(), new ClearFieldValue("x"), new[] { Reason() }));
    }

    [Fact]
    public void Constructor_WithNullLocator_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RejectMessage("m", Provenance(), null!, new ClearFieldValue("x"), new[] { Reason() }));
    }

    [Fact]
    public void Constructor_WithNullRawRecord_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RejectMessage("m", Provenance(), Locator(), null!, new[] { Reason() }));
    }

    [Fact]
    public void Constructor_WithNullReasons_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RejectMessage("m", Provenance(), Locator(), new ClearFieldValue("x"), null!));
    }

    [Fact]
    public void Constructor_WithEmptyReasons_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RejectMessage("m", Provenance(), Locator(), new ClearFieldValue("x"), Array.Empty<RejectReason>()));
    }

    [Fact]
    public void Constructor_WithNullReasonElement_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RejectMessage("m", Provenance(), Locator(), new ClearFieldValue("x"), new RejectReason[] { null! }));
    }
}
