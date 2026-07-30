using Common.FileIngestion.Checkpointing;

namespace Common.FileIngestion.Tests.Checkpointing;

public sealed class WatermarkTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var watermark = new Watermark("file-abc", 1200, 5, 2);

        Assert.Equal("file-abc", watermark.FileId);
        Assert.Equal(1200, watermark.ByteOffset);
        Assert.Equal(5, watermark.LastRecordSeq);
        Assert.Equal(2, watermark.BatchSeq);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankFileId_Throws(string? fileId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Watermark(fileId!, 0, 0, 0));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Constructor_NegativePosition_Throws(long byteOffset, long lastRecordSeq, long batchSeq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Watermark("f", byteOffset, lastRecordSeq, batchSeq));
    }

    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(new Watermark("f", 1, 2, 3), new Watermark("f", 1, 2, 3));
        Assert.NotEqual(new Watermark("f", 1, 2, 3), new Watermark("f", 9, 2, 3));
    }
}
