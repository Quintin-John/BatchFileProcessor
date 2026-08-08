using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Checkpointing;

namespace Common.FileIngestion.Tests.Checkpointing;

public sealed class WatermarkTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var watermark = new Watermark("source.dat", "FILEHASH", 1200, 5, 2);

        Assert.Equal("source.dat", watermark.SourceKey);
        Assert.Equal("FILEHASH", watermark.FileId);
        Assert.Equal(1200, watermark.ByteOffset);
        Assert.Equal(5, watermark.LastRecordSeq);
        Assert.Equal(2, watermark.BatchSeq);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankSourceKey_Throws(string? sourceKey)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Watermark(sourceKey!, "f", 0, 0, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankFileId_Throws(string? fileId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Watermark("src", fileId!, 0, 0, 0));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Constructor_NegativePosition_Throws(long byteOffset, long lastRecordSeq, long batchSeq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Watermark("src", "f", byteOffset, lastRecordSeq, batchSeq));
    }

    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(new Watermark("s", "f", 1, 2, 3), new Watermark("s", "f", 1, 2, 3));
        Assert.NotEqual(new Watermark("s", "f", 1, 2, 3), new Watermark("s", "f", 9, 2, 3));
        Assert.NotEqual(new Watermark("s", "f", 1, 2, 3), new Watermark("s", "OTHER", 1, 2, 3));
    }
}
