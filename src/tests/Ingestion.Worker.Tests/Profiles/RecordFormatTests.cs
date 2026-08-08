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

    // ---------- which layout could frame a file ----------

    private static MemoryStream FileOf(int bytes) => new(new byte[bytes]);

    [Fact]
    public void CanFrame_FixedWidth_AcceptsAWholeNumberOfRecords()
    {
        // The stride is the layout's own record length plus its terminator; a file it describes is some
        // whole number of those and nothing else needs declaring.
        var layout = FixedWidthLayout();
        var stride = layout.RecordLength + layout.TerminatorLength;

        Assert.True(RecordFormats.Resolve("fixed-length")!.CanFrame(layout, FileOf(stride)));
        Assert.True(RecordFormats.Resolve("fixed-length")!.CanFrame(layout, FileOf(stride * 7)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void CanFrame_FixedWidth_RejectsAPartialRecord(int delta)
    {
        var layout = FixedWidthLayout();
        var stride = layout.RecordLength + layout.TerminatorLength;

        Assert.False(RecordFormats.Resolve("fixed-length")!.CanFrame(layout, FileOf(stride * 3 + delta)));
    }

    [Fact]
    public void CanFrame_FixedWidth_RejectsAnEmptyFile()
    {
        // Zero divides by every stride, so an empty file would fit every candidate and decide nothing.
        Assert.False(RecordFormats.Resolve("fixed-length")!.CanFrame(FixedWidthLayout(), FileOf(0)));
    }

    [Fact]
    public void CanFrame_FixedWidth_TellsTwoRecordLengthsApart()
    {
        // The point of the whole mechanism: two versions of one format, distinguished by nothing but the
        // record length each already declares.
        var shortRecords = new Layout("a", 1200, "ascii", 1, 1, 2, new[]
        {
            new RecordDefinition("r", "M", new[] { new FieldDefinition("f", 1, 1200) }),
        });
        var longRecords = new Layout("b", 2400, "ascii", 1, 1, 2, new[]
        {
            new RecordDefinition("r", "M", new[] { new FieldDefinition("f", 1, 2400) }),
        });
        var format = RecordFormats.Resolve("fixed-length")!;

        using var fileOfLongRecords = FileOf((2400 + 1) * 4);

        Assert.True(format.CanFrame(longRecords, fileOfLongRecords));
        Assert.False(format.CanFrame(shortRecords, fileOfLongRecords));
    }

    [Fact]
    public void CanFrame_FixedWidth_AdmitsBothWhenTheStridesShareAMultiple()
    {
        // Not a defect to hide: strides that share a common multiple both divide such a file, and the
        // caller is what makes that safe by demanding a unique fit. Pinned so nobody later "fixes" this
        // into picking one.
        var a = new Layout("a", 1200, "ascii", 1, 1, 2, new[]
        {
            new RecordDefinition("r", "M", new[] { new FieldDefinition("f", 1, 1200) }),
        });
        var b = new Layout("b", 2400, "ascii", 1, 1, 2, new[]
        {
            new RecordDefinition("r", "M", new[] { new FieldDefinition("f", 1, 2400) }),
        });
        var format = RecordFormats.Resolve("fixed-length")!;
        using var shared = FileOf(1201 * 2401); // the first size both strides divide

        Assert.True(format.CanFrame(a, shared));
        Assert.True(format.CanFrame(b, shared));
    }

    [Fact]
    public void CanFrame_Delimited_AdmitsAnyDelimitedLayout()
    {
        // Rows vary in length, so there is nothing structural to divide by. Saying yes to all of them is
        // what makes a profile declaring several delimited layouts fail closed instead of guessing.
        Assert.True(RecordFormats.Resolve("delimited")!.CanFrame(DelimitedLayout(), FileOf(37)));
        Assert.True(RecordFormats.Resolve("delimited")!.CanFrame(DelimitedLayout(), FileOf(0)));
    }

    [Theory]
    [InlineData("fixed-length")]
    [InlineData("delimited")]
    public void CanFrame_AnotherFormatsLayout_NeverFits(string token)
    {
        // Total rather than throwing: the test is asked of every candidate, so the wrong layout type is an
        // answer of "no", not a fault.
        var format = RecordFormats.Resolve(token)!;
        ILayout foreign = token == "fixed-length" ? DelimitedLayout() : FixedWidthLayout();

        Assert.False(format.CanFrame(foreign, FileOf(11)));
    }

    [Theory]
    [InlineData("fixed-length")]
    [InlineData("delimited")]
    public void CanFrame_NullArgument_Throws(string token)
    {
        var format = RecordFormats.Resolve(token)!;

        Assert.Throws<ArgumentNullException>(() => format.CanFrame(null!, FileOf(11)));
        Assert.Throws<ArgumentNullException>(() => format.CanFrame(FixedWidthLayout(), null!));
    }

    [Theory]
    [InlineData("fixed-length")]
    [InlineData("delimited")]
    public void CanFrame_LeavesTheStreamWhereItFoundIt(string token)
    {
        // The selector asks every candidate in turn and then hands the file to the pipeline; a test that
        // consumed the stream would break the next question or the read that follows.
        var format = RecordFormats.Resolve(token)!;
        using var file = FileOf(24);
        file.Position = 6;

        format.CanFrame(token == "fixed-length" ? FixedWidthLayout() : DelimitedLayout(), file);

        Assert.Equal(6, file.Position);
    }

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
