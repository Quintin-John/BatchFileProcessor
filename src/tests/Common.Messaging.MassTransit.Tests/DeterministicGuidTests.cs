namespace Common.Messaging.MassTransit.Tests;

public sealed class DeterministicGuidTests
{
    [Fact]
    public void From_SameName_ReturnsSameGuid()
    {
        Assert.Equal(DeterministicGuid.From("FILEID-0"), DeterministicGuid.From("FILEID-0"));
    }

    [Fact]
    public void From_DifferentNames_ReturnDifferentGuids()
    {
        Assert.NotEqual(DeterministicGuid.From("FILEID-0"), DeterministicGuid.From("FILEID-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_BlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => DeterministicGuid.From(name!));
    }
}
