using Common.FileIngestion.Pipeline;

namespace Common.FileIngestion.Tests.Pipeline;

public sealed class IngestionOptionsTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var options = new IngestionOptions(500, 200_000, 32);

        Assert.Equal(500, options.MaxRecordsPerBatch);
        Assert.Equal(200_000, options.MaxContentBytesPerBatch);
        Assert.Equal(32, options.BatchChannelCapacity);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public void Constructor_InvalidValue_Throws(int maxRecords, int maxContentBytes, int channelCapacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionOptions(maxRecords, maxContentBytes, channelCapacity));
}
