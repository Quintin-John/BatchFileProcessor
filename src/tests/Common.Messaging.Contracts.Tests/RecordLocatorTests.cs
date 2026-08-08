namespace Common.Messaging.Contracts.Tests;

public sealed class RecordLocatorTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;
    private const long FixtureSeq = 101;
    private const long FixtureOffset = FixtureSeq * RecordExtent;
    private const string FixtureType = "TRAN";

    private static RecordLocator Locator(
        long recordSeq = FixtureSeq,
        long byteOffset = FixtureOffset,
        int byteLength = RecordExtent,
        string recordType = FixtureType) => new(recordSeq, byteOffset, byteLength, recordType);

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var locator = Locator();

        Assert.Equal(FixtureSeq, locator.RecordSeq);
        Assert.Equal(FixtureOffset, locator.ByteOffset);
        Assert.Equal(RecordExtent, locator.ByteLength);
        Assert.Equal(FixtureType, locator.RecordType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithRecordSeqBelowOne_Throws(long recordSeq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Locator(recordSeq: recordSeq));
    }

    [Fact]
    public void Constructor_WithNegativeByteOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Locator(byteOffset: -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithByteLengthBelowOne_Throws(int byteLength)
    {
        // A record always occupies at least one byte; a zero extent would make EndByteOffset equal to
        // ByteOffset, so a resume point derived from it would replay the record forever.
        Assert.Throws<ArgumentOutOfRangeException>(() => Locator(byteLength: byteLength));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WithBlankRecordType_Throws(string? recordType)
    {
        Assert.ThrowsAny<ArgumentException>(() => Locator(recordType: recordType!));
    }

    [Fact]
    public void Constructor_WithZeroByteOffset_IsAllowed()
    {
        var locator = Locator(byteOffset: 0);

        Assert.Equal(0, locator.ByteOffset);
    }

    [Fact]
    public void EndByteOffset_IsOffsetPlusLength()
    {
        var locator = Locator();

        Assert.Equal(locator.ByteOffset + locator.ByteLength, locator.EndByteOffset);
    }

    [Fact]
    public void EndByteOffset_OfOneRecord_IsTheNextRecordsOffset()
    {
        // The contract the resume point relies on: consecutive records tile the file with no gap, whatever
        // their individual extents.
        var first = Locator(recordSeq: 1, byteOffset: 0, byteLength: RecordExtent);
        var second = Locator(recordSeq: 2, byteOffset: first.EndByteOffset, byteLength: RecordExtent / 2);

        Assert.Equal(first.ByteLength, second.ByteOffset);
        Assert.Equal(second.ByteOffset + second.ByteLength, second.EndByteOffset);
    }

    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(Locator(), Locator());
        Assert.NotEqual(Locator(), Locator(recordSeq: FixtureSeq + 1));
    }

    [Fact]
    public void Equality_DistinguishesByteLength()
    {
        // Two records at the same offset with different extents are different locations, not the same one.
        Assert.NotEqual(Locator(), Locator(byteLength: RecordExtent + 1));
    }
}
