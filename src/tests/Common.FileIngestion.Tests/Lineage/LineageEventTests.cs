using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class LineageEventTests
{
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Bytes one fixture record occupies, terminator included; the offset is derived from it, not written out.
    private const int RecordExtent = 1200;
    private const long FixtureSeq = 101;

    private static RecordLocator Locator() => new(FixtureSeq, FixtureSeq * RecordExtent, RecordExtent, "TRAN");

    [Fact]
    public void Constructor_SetsProperties()
    {
        var e = new LineageEvent("run-1", "FILE1", Locator(), LineageState.Confirmed, When,
            batch: new BatchReference(3, "FILE1-3"));

        Assert.Equal("run-1", e.CorrelationId);
        Assert.Equal("FILE1", e.FileId);
        Assert.Equal(101, e.Locator.RecordSeq);
        Assert.Equal(LineageState.Confirmed, e.State);
        Assert.Equal(When, e.Timestamp);
        Assert.Equal(3, e.Batch!.BatchSeq);
        Assert.Equal("FILE1-3", e.Batch.MessageId);
    }

    [Fact]
    public void Constructor_OptionalFields_DefaultToNull()
    {
        var e = new LineageEvent("run-1", "FILE1", Locator(), LineageState.Consumed, When);

        Assert.Null(e.Batch);
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
    public void Constructor_BlankReasonCode_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new LineageEvent("run-1", "FILE1", Locator(), LineageState.Rejected, When, reasonCode: " "));
}
