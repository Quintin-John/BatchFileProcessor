using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class LineageEventTests
{
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static RecordLocator Locator() => new(101, 121200, "TRAN");

    [Fact]
    public void Constructor_SetsProperties()
    {
        var e = new LineageEvent("run-1", "FILE1", Locator(), LineageState.Confirmed, When,
            batchSeq: 3, messageId: "FILE1-3", reasonCode: null);

        Assert.Equal("run-1", e.CorrelationId);
        Assert.Equal("FILE1", e.FileId);
        Assert.Equal(101, e.Locator.RecordSeq);
        Assert.Equal(LineageState.Confirmed, e.State);
        Assert.Equal(When, e.Timestamp);
        Assert.Equal(3, e.BatchSeq);
        Assert.Equal("FILE1-3", e.MessageId);
    }

    [Fact]
    public void Constructor_OptionalFields_DefaultToNull()
    {
        var e = new LineageEvent("run-1", "FILE1", Locator(), LineageState.Consumed, When);

        Assert.Null(e.BatchSeq);
        Assert.Null(e.MessageId);
        Assert.Null(e.ReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Constructor_BlankCorrelationId_Throws(string? correlationId) =>
        Assert.ThrowsAny<ArgumentException>(
            () => new LineageEvent(correlationId!, "FILE1", Locator(), LineageState.Consumed, When));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Constructor_BlankFileId_Throws(string? fileId) =>
        Assert.ThrowsAny<ArgumentException>(
            () => new LineageEvent("run-1", fileId!, Locator(), LineageState.Consumed, When));

    [Fact]
    public void Constructor_NullLocator_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new LineageEvent("run-1", "FILE1", null!, LineageState.Consumed, When));

    [Fact]
    public void Constructor_UndefinedState_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new LineageEvent("run-1", "FILE1", Locator(), (LineageState)999, When));

    [Fact]
    public void Constructor_NegativeBatchSeq_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LineageEvent("run-1", "FILE1", Locator(), LineageState.Batched, When, batchSeq: -1));

    [Fact]
    public void Constructor_BlankMessageId_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new LineageEvent("run-1", "FILE1", Locator(), LineageState.Batched, When, messageId: " "));

    [Fact]
    public void Constructor_BlankReasonCode_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new LineageEvent("run-1", "FILE1", Locator(), LineageState.Rejected, When, reasonCode: " "));
}
