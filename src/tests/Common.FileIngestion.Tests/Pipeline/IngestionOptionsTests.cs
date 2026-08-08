using Common.FileIngestion.Pipeline;

namespace Common.FileIngestion.Tests.Pipeline;

public sealed class IngestionOptionsTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var options = new IngestionOptions(500, 200_000, 32, 4, 64);

        Assert.Equal(500, options.MaxRecordsPerBatch);
        Assert.Equal(200_000, options.MaxContentBytesPerBatch);
        Assert.Equal(32, options.BatchChannelCapacity);
        Assert.Equal(4, options.PublisherConcurrency);
        Assert.Equal(64, options.PublisherConfirmWindow);
    }

    [Theory]
    [InlineData(0, 1, 1, 1, 1)]
    [InlineData(1, 0, 1, 1, 1)]
    [InlineData(1, 1, 0, 1, 1)]
    [InlineData(1, 1, 1, 0, 1)]
    [InlineData(1, 1, 1, 1, 0)]
    public void Constructor_InvalidValue_Throws(int maxRecords, int maxContentBytes, int cap, int publishers, int window) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionOptions(maxRecords, maxContentBytes, cap, publishers, window));
}
