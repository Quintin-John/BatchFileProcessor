using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class DelimitedLayoutTests
{
    private const string Version = "1.0";
    private const string Encoding = "ascii";
    private const char Tab = '\t';

    private static DelimitedFieldDefinition Field(string name, int index) => new(name, index);

    private static DelimitedFieldDefinition[] Fields(int count) =>
        Enumerable.Range(0, count).Select(i => Field($"f{i}", i)).ToArray();

    private static DelimitedRowDefinition Data(int fieldCount = 3) =>
        new("data", RowRole.Data, rows: 0, Fields(fieldCount));

    private static DelimitedRowDefinition Header(int rows = 1, bool skip = true) =>
        new("header", RowRole.Header, rows, skip ? [] : Fields(2), skip);

    private static DelimitedRowDefinition Trailer(int rows = 1, bool skip = true) =>
        new("trailer", RowRole.Trailer, rows, skip ? [] : Fields(2), skip);

    private static DelimitedLayout Layout(params DelimitedRowDefinition[] rows) =>
        new(Version, Tab, Encoding, rows.Length == 0 ? [Data()] : rows);

    // ---------- construction ----------

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var layout = Layout(Header(), Data(), Trailer());

        Assert.Equal(Version, layout.Version);
        Assert.Equal(Tab, layout.Delimiter);
        Assert.Equal(Encoding, layout.Encoding);
        Assert.Equal(3, layout.RowTypes.Count);
        Assert.Equal(RowRole.Data, layout.Data.Role);
        Assert.NotNull(layout.Header);
        Assert.NotNull(layout.Trailer);
    }

    [Fact]
    public void Constructor_WithoutHeaderOrTrailer_LeavesThemNullAndRowCountsZero()
    {
        var layout = Layout(Data());

        Assert.Null(layout.Header);
        Assert.Null(layout.Trailer);
        Assert.Equal(0, layout.HeaderRows);
        Assert.Equal(0, layout.TrailerRows);
    }

    [Fact]
    public void HeaderAndTrailerRows_ComeFromTheirRowTypes()
    {
        const int headerRows = 2;
        const int trailerRows = 3;

        var layout = Layout(Header(headerRows), Data(), Trailer(trailerRows));

        Assert.Equal(headerRows, layout.HeaderRows);
        Assert.Equal(trailerRows, layout.TrailerRows);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankVersion_Throws(string? version)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DelimitedLayout(version!, Tab, Encoding, [Data()]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankEncoding_Throws(string? encoding)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DelimitedLayout(Version, Tab, encoding!, [Data()]));
    }

    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    public void Constructor_LineTerminatorDelimiter_Throws(char delimiter)
    {
        Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, delimiter, Encoding, [Data()]));
    }

    [Fact]
    public void Constructor_NullRowTypes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DelimitedLayout(Version, Tab, Encoding, null!));
    }

    [Fact]
    public void Constructor_EmptyRowTypes_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, Encoding, []));
    }

    [Fact]
    public void Constructor_NullRowTypeElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DelimitedLayout(Version, Tab, Encoding, new DelimitedRowDefinition[] { null! }));
    }

    [Fact]
    public void Constructor_WithoutDataRole_Throws()
    {
        // A layout with no body would emit nothing; that is a mis-transcription, not a valid file shape.
        var ex = Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, Encoding, [Header()]));
        Assert.Contains("role 'data'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RowRole.Header)]
    [InlineData(RowRole.Data)]
    [InlineData(RowRole.Trailer)]
    public void Constructor_DuplicateRole_Throws(RowRole role)
    {
        // Row assignment is positional, so two types sharing a role leaves no way to say which rows are whose.
        DelimitedRowDefinition First() => new("first", role, role == RowRole.Data ? 0 : 1, Fields(2));
        DelimitedRowDefinition Second() => new("second", role, role == RowRole.Data ? 0 : 1, Fields(2));

        var rows = role == RowRole.Data
            ? new[] { First(), Second() }
            : [First(), Second(), Data()];

        var ex = Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, Encoding, rows));
        Assert.Contains("ambiguous", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_DuplicateRowTypeName_Throws()
    {
        var rows = new[]
        {
            new DelimitedRowDefinition("same", RowRole.Header, 1, Fields(2)),
            new DelimitedRowDefinition("same", RowRole.Data, 0, Fields(2)),
        };

        var ex = Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, Encoding, rows));
        Assert.Contains("Duplicate row type name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowTypes_AreDefensivelyCopied()
    {
        var source = new List<DelimitedRowDefinition> { Data() };
        var layout = new DelimitedLayout(Version, Tab, Encoding, source);

        source.Add(Header());

        Assert.Single(layout.RowTypes);
    }

    // ---------- positional row resolution ----------

    [Fact]
    public void ResolveByPosition_WithHeaderAndTrailer_AssignsByPosition()
    {
        const int headerRows = 2;
        const int trailerRows = 1;
        const long totalRows = 6;
        var layout = Layout(Header(headerRows), Data(), Trailer(trailerRows));

        var roles = Enumerable.Range(0, (int)totalRows)
            .Select(i => layout.ResolveByPosition(i, totalRows)!.Role)
            .ToArray();

        Assert.Equal(
            [RowRole.Header, RowRole.Header, RowRole.Data, RowRole.Data, RowRole.Data, RowRole.Trailer],
            roles);
    }

    [Fact]
    public void ResolveByPosition_WithNoHeaderOrTrailer_IsAllData()
    {
        const long totalRows = 3;
        var layout = Layout(Data());

        Assert.All(
            Enumerable.Range(0, (int)totalRows),
            i => Assert.Equal(RowRole.Data, layout.ResolveByPosition(i, totalRows)!.Role));
    }

    [Fact]
    public void ResolveByPosition_WhenHeaderAndTrailerExceedTheFile_ReturnsNull()
    {
        // A file too short to hold its own declared header and trailer is malformed data, not a code fault:
        // the caller quarantines it rather than the run faulting.
        var layout = Layout(Header(2), Data(), Trailer(2));

        Assert.Null(layout.ResolveByPosition(0, totalRows: 3));
    }

    [Fact]
    public void ResolveByPosition_WhenFileIsExactlyHeaderPlusTrailer_HasNoDataRows()
    {
        const long totalRows = 2;
        var layout = Layout(Header(1), Data(), Trailer(1));

        Assert.Equal(RowRole.Header, layout.ResolveByPosition(0, totalRows)!.Role);
        Assert.Equal(RowRole.Trailer, layout.ResolveByPosition(1, totalRows)!.Role);
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, 3)]
    [InlineData(0, 0)]
    public void ResolveByPosition_OutOfRange_Throws(long rowIndex, long totalRows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Layout(Data()).ResolveByPosition(rowIndex, totalRows));
    }
}
