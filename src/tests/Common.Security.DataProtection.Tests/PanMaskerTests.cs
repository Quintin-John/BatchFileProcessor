namespace Common.Security.DataProtection.Tests;

public sealed class PanMaskerTests
{
    private static readonly PanMasker Masker = new();

    [Fact]
    public void Name_IsStrategyIdentifier() => Assert.Equal("first6last4", Masker.Name);

    [Fact]
    public void Mask_LongValue_RevealsFirstSixAndLastFour()
    {
        Assert.Equal("123456******3456", Masker.Mask("1234567890123456"));
    }

    [Fact]
    public void Mask_ElevenChars_MasksOnlyTheMiddle()
    {
        Assert.Equal("123456*8901", Masker.Mask("12345678901"));
    }

    [Theory]
    [InlineData("1234567890", "**********")] // exactly 10 -> fully masked
    [InlineData("123", "***")]
    [InlineData("1", "*")]
    public void Mask_ShortValue_IsFullyMasked(string input, string expected)
    {
        Assert.Equal(expected, Masker.Mask(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Mask_NullOrEmpty_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, Masker.Mask(input));
    }
}
