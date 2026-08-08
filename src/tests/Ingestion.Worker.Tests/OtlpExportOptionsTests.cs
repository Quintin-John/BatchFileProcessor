namespace Ingestion.Worker.Tests;

public sealed class OtlpExportOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEndpoint_Unset_ReturnsNull(string? endpoint) =>
        Assert.Null(new OtlpExportOptions { Endpoint = endpoint }.ResolveEndpoint());

    [Fact]
    public void ResolveEndpoint_ValidAbsoluteUri_ReturnsIt()
    {
        var uri = new OtlpExportOptions { Endpoint = "http://collector:4317" }.ResolveEndpoint();

        Assert.Equal(new Uri("http://collector:4317"), uri);
    }

    [Theory]
    [InlineData("relative/only")]
    [InlineData("not a uri")]
    public void ResolveEndpoint_NotAbsolute_Throws(string endpoint) =>
        Assert.Throws<InvalidOperationException>(() => new OtlpExportOptions { Endpoint = endpoint }.ResolveEndpoint());
}
