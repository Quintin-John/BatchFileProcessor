namespace Common.Messaging.Contracts.Tests;

public sealed class RecordLocatorTests
{
    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var locator = new RecordLocator(101, 121200, "TRAN");

        Assert.Equal(101, locator.RecordSeq);
        Assert.Equal(121200, locator.ByteOffset);
        Assert.Equal("TRAN", locator.RecordType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithRecordSeqBelowOne_Throws(long recordSeq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordLocator(recordSeq, 0, "TRAN"));
    }

    [Fact]
    public void Constructor_WithNegativeByteOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordLocator(1, -1, "TRAN"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WithBlankRecordType_Throws(string? recordType)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RecordLocator(1, 0, recordType!));
    }

    [Fact]
    public void Constructor_WithZeroByteOffset_IsAllowed()
    {
        var locator = new RecordLocator(1, 0, "HEAD");

        Assert.Equal(0, locator.ByteOffset);
    }

    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(new RecordLocator(1, 0, "TRAN"), new RecordLocator(1, 0, "TRAN"));
        Assert.NotEqual(new RecordLocator(1, 0, "TRAN"), new RecordLocator(2, 0, "TRAN"));
    }
}
