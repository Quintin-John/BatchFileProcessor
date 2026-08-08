using Common.FileIngestion.Lineage;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class BatchReferenceTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var batch = new BatchReference(3, "FILE1-3");

        Assert.Equal(3, batch.BatchSeq);
        Assert.Equal("FILE1-3", batch.MessageId);
    }

    [Fact]
    public void Constructor_NegativeBatchSeq_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatchReference(-1, "FILE1-3"));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Constructor_BlankMessageId_Throws(string? messageId) =>
        Assert.ThrowsAny<ArgumentException>(() => new BatchReference(0, messageId!));
}
