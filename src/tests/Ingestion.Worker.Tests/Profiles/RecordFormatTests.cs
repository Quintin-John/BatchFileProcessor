using System.Text;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.FileIngestion.Reading;
using Ingestion.Worker.Profiles;

namespace Ingestion.Worker.Tests.Profiles;

public sealed class RecordFormatTests
{
    private static Layout FixedWidthLayout() => new("4.8", 10, "ascii", 1, 1, 2, new[]
    {
        new RecordDefinition("r", "M", new[] { new FieldDefinition("f", 1, 10) }),
    });

    private static DelimitedLayout DelimitedLayout() => new("1.0", "\t", '\n', "ascii", new[]
    {
        new DelimitedRowDefinition("body", RowRole.Data, 0, new[] { new DelimitedFieldDefinition("f", 0) }),
    });

    // ---------- registry ----------

    [Theory]
    [InlineData("fixed-length")]
    [InlineData("delimited")]
    public void Resolve_RegisteredToken_ReturnsItsFormat(string token)
    {
        Assert.Equal(token, RecordFormats.Resolve(token)!.Token);
    }

    [Theory]
    [InlineData("FIXED-LENGTH")]
    [InlineData("Delimited")]
    public void Resolve_IsCaseInsensitive(string token)
    {
        Assert.NotNull(RecordFormats.Resolve(token));
    }

    [Theory]
    [InlineData("csv")]         // not a token: the delimiter is a layout concern, not a format
    [InlineData("fixed")]
    [InlineData("")]
    public void Resolve_UnregisteredToken_ReturnsNull(string token)
    {
        Assert.Null(RecordFormats.Resolve(token));
    }

    [Fact]
    public void Resolve_NullToken_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RecordFormats.Resolve(null!));
    }

    [Fact]
    public void Tokens_ListsEveryRegisteredFormat()
    {
        Assert.Equal(["fixed-length", "delimited"], RecordFormats.Tokens);
    }

    [Fact]
    public void EveryRegisteredToken_ResolvesToAFormatDeclaringThatSameToken()
    {
        // Guards the registry against an entry keyed by one token but reporting another.
        Assert.All(RecordFormats.Tokens, token => Assert.Equal(token, RecordFormats.Resolve(token)!.Token));
    }

    // ---------- fixed-length binding ----------

    [Fact]
    public void FixedLength_CreateFraming_PairsTheFixedWidthReaderAndParser()
    {
        var (reader, parser) = new FixedLengthFormat().CreateFraming(FixedWidthLayout(), Encoding.ASCII);

        Assert.IsType<StreamRecordReader>(reader);
        Assert.IsType<FixedLengthRecordParser>(parser);
    }

    [Fact]
    public void FixedLength_CreateFraming_WithADelimitedLayout_Throws()
    {
        // Unreachable through a profile, whose format loads its own layout, but a mismatched pairing must
        // fail at composition rather than misread every record.
        var ex = Assert.Throws<InvalidOperationException>(
            () => new FixedLengthFormat().CreateFraming(DelimitedLayout(), Encoding.ASCII));
        Assert.Contains("requires a fixed-length layout", ex.Message, StringComparison.Ordinal);
    }

    // ---------- delimited binding ----------

    [Fact]
    public void Delimited_CreateFraming_PairsTheDelimitedReaderAndParser()
    {
        var (reader, parser) = new DelimitedFormat().CreateFraming(DelimitedLayout(), Encoding.ASCII);

        Assert.IsType<DelimitedLineReader>(reader);
        Assert.IsType<DelimitedRecordParser>(parser);
    }

    [Fact]
    public void Delimited_CreateFraming_WithAFixedWidthLayout_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new DelimitedFormat().CreateFraming(FixedWidthLayout(), Encoding.ASCII));
        Assert.Contains("requires a delimited layout", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFraming_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new DelimitedFormat().CreateFraming(null!, Encoding.ASCII));
        Assert.Throws<ArgumentNullException>(() => new DelimitedFormat().CreateFraming(DelimitedLayout(), null!));
        Assert.Throws<ArgumentNullException>(() => new FixedLengthFormat().CreateFraming(null!, Encoding.ASCII));
        Assert.Throws<ArgumentNullException>(() => new FixedLengthFormat().CreateFraming(FixedWidthLayout(), null!));
    }

    // ---------- loading ----------

    private const string FixedWidthYaml = """
        version: "1.0"
        recordLength: 4
        encoding: ascii
        discriminator: { start: 1, length: 2 }
        recordTypes:
          r:
            match: "MM"
            fields: [ { name: f, start: 1, length: 4 } ]
        """;

    private const string DelimitedYaml = """
        version: "1.0"
        delimiter: ","
        encoding: ascii
        rowTypes:
          r:
            role: data
            fields: [ { name: f, index: 0 } ]
        """;

    [Fact]
    public void EachFormat_LoadsItsOwnLayoutDialect_IntoItsOwnModel()
    {
        // Loading and framing come from the same object, which is what makes a format unable to hand its
        // reader a layout the reader cannot frame. Both models satisfy the shared surface, so protection
        // and redaction treat them identically.
        Assert.IsType<Layout>(LoadThroughFormat(new FixedLengthFormat(), FixedWidthYaml));
        Assert.IsType<DelimitedLayout>(LoadThroughFormat(new DelimitedFormat(), DelimitedYaml));
    }

    [Fact]
    public void AFormat_RejectsTheOtherDialect_RatherThanMisreadingIt()
    {
        Assert.ThrowsAny<Exception>(() => LoadThroughFormat(new FixedLengthFormat(), DelimitedYaml));
        Assert.ThrowsAny<Exception>(() => LoadThroughFormat(new DelimitedFormat(), FixedWidthYaml));
    }

    private static ILayout LoadThroughFormat(IRecordFormat format, string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), "layout-" + Guid.NewGuid().ToString("N") + ".yaml");
        try
        {
            File.WriteAllText(path, yaml);
            return format.LoadLayout(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
