using Common.FileIngestion.Abstractions;

namespace Common.FileIngestion.Tests.Parsing;

public sealed class RecordParseResultTests
{
    [Fact]
    public void Skipped_IsSkipped_NotSuccess_NoRecord()
    {
        var result = RecordParseResult.Skipped("HEAD");

        Assert.True(result.IsSkipped);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Record);
        Assert.Equal("HEAD", result.RecordType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Skipped_BlankType_Throws(string? recordType) =>
        Assert.ThrowsAny<ArgumentException>(() => RecordParseResult.Skipped(recordType!));
}
