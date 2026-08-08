namespace Common.Messaging.Contracts.Tests;

public sealed class MessageProvenanceTests
{
    private static MessageProvenance CreateValid() =>
        new("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var provenance = new MessageProvenance("run-xyz", "file-abc", "source.dat", "feed-a", "1.0");

        Assert.Equal("run-xyz", provenance.CorrelationId);
        Assert.Equal("file-abc", provenance.FileId);
        Assert.Equal("source.dat", provenance.FileName);
        Assert.Equal("feed-a", provenance.Profile);
        Assert.Equal("1.0", provenance.LayoutVersion);
    }

    [Theory]
    [InlineData(null, "f", "n", "p", "v")]
    [InlineData("", "f", "n", "p", "v")]
    [InlineData("c", "  ", "n", "p", "v")]
    [InlineData("c", "f", null, "p", "v")]
    [InlineData("c", "f", "n", "", "v")]
    [InlineData("c", "f", "n", "p", "  ")]
    public void Constructor_WithBlankArgument_Throws(
        string? correlationId, string? fileId, string? fileName, string? profile, string? layoutVersion)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new MessageProvenance(correlationId!, fileId!, fileName!, profile!, layoutVersion!));
    }

    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(CreateValid(), CreateValid());
        Assert.NotEqual(CreateValid(), new MessageProvenance("run-xyz", "file-abc", "source.dat", "feed-a", "2.0"));
    }
}
