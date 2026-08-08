using System.Text;
using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class LayoutEncodingTests
{
    [Theory]
    [InlineData("ascii")]
    [InlineData("utf-8")]
    [InlineData("iso-8859-1")]
    public void Resolve_AnEncodingTheFrameworkShipsInTheBox_Succeeds(string name)
    {
        Assert.Equal(Encoding.GetEncoding(name).CodePage, LayoutEncoding.Resolve(name).CodePage);
    }

    [Theory]
    [InlineData("ibm037")]        // EBCDIC — the shape a mainframe extract arrives in
    [InlineData("windows-1252")]  // regional single-byte page
    [InlineData("ibm500")]
    public void Resolve_AnEncodingBehindTheCodePageProvider_Succeeds(string name)
    {
        // Without the provider registered these throw, so a layout declaring one is unusable no matter how
        // valid the declaration is. This is the whole reason the resolver exists.
        var encoding = LayoutEncoding.Resolve(name);

        Assert.True(encoding.IsSingleByte);
        Assert.NotEqual(Encoding.ASCII.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Resolve_RoundTripsThroughTheResolvedEncoding_NotTheDefaultOne()
    {
        // Proves the resolver returns a genuinely different codec rather than silently falling back:
        // EBCDIC encodes 'A' as 0xC1, ASCII as 0x41.
        var ebcdic = LayoutEncoding.Resolve("ibm037");

        Assert.Equal(0xC1, ebcdic.GetBytes("A")[0]);
        Assert.Equal("A", ebcdic.GetString([0xC1]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => LayoutEncoding.Resolve(name!));
    }

    [Fact]
    public void Resolve_AnEncodingThePlatformCannotSupply_FailsClosed_NamingTheDeclaration()
    {
        const string declared = "not-a-real-encoding";

        var ex = Assert.Throws<ArgumentException>(() => LayoutEncoding.Resolve(declared));

        // The diagnostic must point at the layout, not surface a bare framework message.
        Assert.Contains(declared, ex.Message, StringComparison.Ordinal);
    }
}
