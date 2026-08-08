namespace Common.Security.DataProtection.Tests;

public sealed class FieldProtectionContextTests
{
    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var context = new FieldProtectionContext("file-abc", 101, "encrypted");

        Assert.Equal("file-abc", context.FileId);
        Assert.Equal(101, context.RecordSeq);
        Assert.Equal("encrypted", context.Field);
    }

    [Theory]
    [InlineData(null, "encrypted")]
    [InlineData("", "encrypted")]
    [InlineData("file", "  ")]
    public void Constructor_WithBlankArgument_Throws(string? fileId, string? field)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FieldProtectionContext(fileId!, 1, field!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithRecordSeqBelowOne_Throws(long recordSeq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldProtectionContext("file", recordSeq, "f"));
    }
}
