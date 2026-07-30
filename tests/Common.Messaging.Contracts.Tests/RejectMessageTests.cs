namespace Common.Messaging.Contracts.Tests;

public sealed class RejectMessageTests
{
    private static RejectReason Reason() => new("amount", "decimal", "NON_NUMERIC");

    private static RejectMessage Create(IReadOnlyList<RejectReason>? reasons = null) =>
        new(
            messageId: "file-abc-101-reject",
            correlationId: "run-xyz",
            fileId: "file-abc",
            fileName: "g266.dat",
            profile: "g266",
            layoutVersion: "4.8",
            recordSeq: 101,
            byteOffset: 121200,
            recordType: "TRAN",
            rawRecord: new ClearFieldValue("cmF3LXJlY29yZA=="),
            reasons: reasons ?? new[] { Reason() });

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var message = Create();

        Assert.Equal("file-abc-101-reject", message.MessageId);
        Assert.Equal("run-xyz", message.CorrelationId);
        Assert.Equal("file-abc", message.FileId);
        Assert.Equal("g266.dat", message.FileName);
        Assert.Equal("g266", message.Profile);
        Assert.Equal("4.8", message.LayoutVersion);
        Assert.Equal(101, message.RecordSeq);
        Assert.Equal(121200, message.ByteOffset);
        Assert.Equal("TRAN", message.RecordType);
        Assert.Equal(new ClearFieldValue("cmF3LXJlY29yZA=="), message.RawRecord);
        Assert.Single(message.Reasons);
    }

    [Fact]
    public void RawRecord_AcceptsEncryptedContent()
    {
        var encrypted = new EncryptedFieldValue(
            new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        var message = new RejectMessage(
            "m", "c", "f", "n", "p", "v", 1, 0, "TRAN", encrypted, new[] { Reason() });

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
    [InlineData(null, "c", "f", "n", "p", "v", "TRAN")]
    [InlineData("m", "", "f", "n", "p", "v", "TRAN")]
    [InlineData("m", "c", "  ", "n", "p", "v", "TRAN")]
    [InlineData("m", "c", "f", null, "p", "v", "TRAN")]
    [InlineData("m", "c", "f", "n", "", "v", "TRAN")]
    [InlineData("m", "c", "f", "n", "p", "  ", "TRAN")]
    [InlineData("m", "c", "f", "n", "p", "v", null)]
    public void Constructor_WithBlankIdentity_Throws(
        string? messageId, string? correlationId, string? fileId, string? fileName,
        string? profile, string? layoutVersion, string? recordType)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RejectMessage(
            messageId!, correlationId!, fileId!, fileName!, profile!, layoutVersion!,
            1, 0, recordType!, new ClearFieldValue("x"), new[] { Reason() }));
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, -1L)]
    public void Constructor_WithOutOfRangePosition_Throws(long recordSeq, long byteOffset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RejectMessage(
            "m", "c", "f", "n", "p", "v", recordSeq, byteOffset, "TRAN",
            new ClearFieldValue("x"), new[] { Reason() }));
    }

    [Fact]
    public void Constructor_WithNullRawRecord_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RejectMessage(
            "m", "c", "f", "n", "p", "v", 1, 0, "TRAN", null!, new[] { Reason() }));
    }

    [Fact]
    public void Constructor_WithNullReasons_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RejectMessage(
            "m", "c", "f", "n", "p", "v", 1, 0, "TRAN", new ClearFieldValue("x"), null!));
    }

    [Fact]
    public void Constructor_WithEmptyReasons_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RejectMessage(
            "m", "c", "f", "n", "p", "v", 1, 0, "TRAN", new ClearFieldValue("x"), Array.Empty<RejectReason>()));
    }

    [Fact]
    public void Constructor_WithNullReasonElement_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RejectMessage(
            "m", "c", "f", "n", "p", "v", 1, 0, "TRAN", new ClearFieldValue("x"), new RejectReason[] { null! }));
    }
}
