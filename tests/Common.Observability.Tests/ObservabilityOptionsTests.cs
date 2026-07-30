namespace Common.Observability.Tests;

public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var options = new ObservabilityOptions { ServiceName = "svc", SamplingRatio = 0.5 };

        options.Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_BlankServiceName_Throws(string? serviceName)
    {
        var options = new ObservabilityOptions { ServiceName = serviceName! };

        Assert.ThrowsAny<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Validate_SamplingRatioOutOfRange_Throws(double ratio)
    {
        var options = new ObservabilityOptions { ServiceName = "svc", SamplingRatio = ratio };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}
