using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class DelimitedLayoutTests
{
    private const string Version = "1.0";
    private const string Encoding = "ascii";
    private const string Tab = "\t";
    private const char Terminator = '\n';

    private static DelimitedFieldDefinition Field(string name, int index) => new(name, index);

    private static DelimitedFieldDefinition[] Fields(int count) =>
        Enumerable.Range(0, count).Select(i => Field($"f{i}", i)).ToArray();

    private static DelimitedRowDefinition Data(int fieldCount = 3) =>
        new("data", RowRole.Data, rows: 0, Fields(fieldCount));

    // A body type that names itself, so several can share the body of one file.
    private static DelimitedRowDefinition Data(string name, string marker, int markerIndex = 0) =>
        new(name, RowRole.Data, rows: 0, Fields(3), skip: false, new RowMatch(markerIndex, marker));

    private static DelimitedRowDefinition Header(int rows = 1, bool skip = true) =>
        new("header", RowRole.Header, rows, skip ? [] : Fields(2), skip);

    private static DelimitedRowDefinition Trailer(int rows = 1, bool skip = true) =>
        new("trailer", RowRole.Trailer, rows, skip ? [] : Fields(2), skip);

    private static DelimitedLayout Layout(params DelimitedRowDefinition[] rows) =>
        new(Version, Tab, Terminator, Encoding, rows.Length == 0 ? [Data()] : rows);

    // ---------- construction ----------

    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        var layout = Layout(Header(), Data(), Trailer());

        Assert.Equal(Version, layout.Version);
        Assert.Equal(Tab, layout.Delimiter);
        Assert.Equal(Encoding, layout.Encoding);
        Assert.Equal(3, layout.RowTypes.Count);
        Assert.Equal(RowRole.Data, Assert.Single(layout.DataRows).Role);
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
        Assert.ThrowsAny<ArgumentException>(() => new DelimitedLayout(version!, Tab, '\n', Encoding, [Data()]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankEncoding_Throws(string? encoding)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DelimitedLayout(Version, Tab, '\n', encoding!, [Data()]));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("~\n~")]   // buried inside a longer delimiter, not just standing alone
    public void Constructor_LineTerminatorInDelimiter_Throws(string delimiter)
    {
        Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, delimiter, '\n', Encoding, [Data()]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_EmptyDelimiter_Throws(string? delimiter)
    {
        // An empty delimiter has no boundary to find, so every row would read as one field.
        Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, delimiter!, '\n', Encoding, [Data()]));
    }

    [Theory]
    [InlineData("~", '~')]        // the delimiter is the terminator
    [InlineData("~|~", '|')]      // the terminator is buried inside a longer delimiter
    public void Constructor_RowTerminatorInsideTheDelimiter_Throws(string delimiter, char rowTerminator)
    {
        // Framing would end the row part-way through a field boundary, so the two would disagree about
        // where the row stops.
        var ex = Assert.Throws<ArgumentException>(
            () => new DelimitedLayout(Version, delimiter, rowTerminator, Encoding, [Data()]));
        Assert.Contains("row terminator", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_MultiCharacterDelimiter_IsAccepted()
    {
        // A delimiter is text: nothing in the layout restricts a feed to single-character separators.
        Assert.Equal("~|~", new DelimitedLayout(Version, "~|~", '\n', Encoding, [Data()]).Delimiter);
    }

    [Fact]
    public void Constructor_NullRowTypes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DelimitedLayout(Version, Tab, '\n', Encoding, null!));
    }

    [Fact]
    public void Constructor_EmptyRowTypes_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, '\n', Encoding, []));
    }

    [Fact]
    public void Constructor_NullRowTypeElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DelimitedLayout(Version, Tab, '\n', Encoding, new DelimitedRowDefinition[] { null! }));
    }

    [Fact]
    public void Constructor_WithoutDataRole_Throws()
    {
        // A layout with no body would emit nothing; that is a mis-transcription, not a valid file shape.
        var ex = Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, '\n', Encoding, [Header()]));
        Assert.Contains("role 'data'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RowRole.Header)]
    [InlineData(RowRole.Trailer)]
    public void Constructor_DuplicatePositionalRole_Throws(RowRole role)
    {
        // Header and trailer are assigned by position, so two types sharing one of those roles leaves no way
        // to say which rows are whose. Data is exempt: body rows name themselves.
        var rows = new[]
        {
            new DelimitedRowDefinition("first", role, 1, Fields(2)),
            new DelimitedRowDefinition("second", role, 1, Fields(2)),
            Data(),
        };

        var ex = Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, '\n', Encoding, rows));
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

        var ex = Assert.Throws<ArgumentException>(() => new DelimitedLayout(Version, Tab, '\n', Encoding, rows));
        Assert.Contains("Duplicate row type name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowTypes_AreDefensivelyCopied()
    {
        var source = new List<DelimitedRowDefinition> { Data() };
        var layout = new DelimitedLayout(Version, Tab, '\n', Encoding, source);

        source.Add(Header());

        Assert.Single(layout.RowTypes);
    }

    // ---------- positional row resolution ----------

    [Fact]
    public void ASingleBodyType_NeedsNoMarker_AndEveryBodyRowIsIt()
    {
        // The ordinary shape: position identifies the body, so no column is read to classify it.
        var layout = Layout(Data());

        Assert.Null(layout.DataMarkerIndex);
        Assert.Equal("data", layout.ResolveDataRow("a\tb\tc")!.Name);
    }

    [Fact]
    public void SeveralBodyTypes_AreResolvedByTheMarkerTheRowCarries()
    {
        var layout = Layout(Data("debit", "DR"), Data("credit", "CR"));

        Assert.Equal(0, layout.DataMarkerIndex);
        Assert.Equal("debit", layout.ResolveDataRow("DR\tb\tc")!.Name);
        Assert.Equal("credit", layout.ResolveDataRow("CR\tb\tc")!.Name);
    }

    [Fact]
    public void TheMarkerIsReadFromWhicheverColumnTheLayoutNames()
    {
        // The column carrying the marker is a property of the feed, not a position the engine assumes.
        var layout = Layout(Data("debit", "DR", markerIndex: 2), Data("credit", "CR", markerIndex: 2));

        Assert.Equal(2, layout.DataMarkerIndex);
        Assert.Equal("credit", layout.ResolveDataRow("a\tb\tCR")!.Name);
    }

    [Fact]
    public void ABodyRowWhoseMarkerNamesNoType_ResolvesToNull()
    {
        var layout = Layout(Data("debit", "DR"), Data("credit", "CR"));

        Assert.Null(layout.ResolveDataRow("XX\tb\tc"));
    }

    [Fact]
    public void ABodyRowTooShortToCarryTheMarker_ResolvesToNull()
    {
        var layout = Layout(Data("debit", "DR", markerIndex: 2), Data("credit", "CR", markerIndex: 2));

        Assert.Null(layout.ResolveDataRow("a\tb"));
    }

    [Fact]
    public void ASingleBodyTypeMayStillDeclareAMarker_AndThenEveryBodyRowIsCheckedAgainstIt()
    {
        var layout = Layout(Data("debit", "DR"));

        Assert.Equal(0, layout.DataMarkerIndex);
        Assert.Equal("debit", layout.ResolveDataRow("DR\tb\tc")!.Name);
        Assert.Null(layout.ResolveDataRow("XX\tb\tc"));
    }

    [Fact]
    public void MarkersAreComparedWhole_NotByPrefix()
    {
        var layout = Layout(Data("short", "D"), Data("long", "DR"));

        Assert.Equal("short", layout.ResolveDataRow("D\tb\tc")!.Name);
        Assert.Equal("long", layout.ResolveDataRow("DR\tb\tc")!.Name);
    }

    // ---------- what a body of several types must satisfy ----------

    [Fact]
    public void SeveralBodyTypes_WhereOneDeclaresNoMarker_Throws()
    {
        // Without a marker that type could never be identified, so the layout is unsatisfiable rather than
        // merely unusual.
        var ex = Assert.Throws<ArgumentException>(
            () => Layout(Data("debit", "DR"), new DelimitedRowDefinition("plain", RowRole.Data, 0, Fields(3))));
        Assert.Contains("must name itself with a marker", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyTypesCarryingMarkersInDifferentColumns_Throw()
    {
        // Otherwise resolution would mean trying each type in turn, and which one a row matched would depend
        // on the order they happened to be declared in.
        var ex = Assert.Throws<ArgumentException>(
            () => Layout(Data("debit", "DR"), Data("credit", "CR", markerIndex: 1)));
        Assert.Contains("identified by the same field", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoBodyTypesClaimingTheSameMarker_Throw()
    {
        var ex = Assert.Throws<ArgumentException>(() => Layout(Data("debit", "DR"), Data("other", "DR")));
        Assert.Contains("both claim marker", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALayoutWithNoBodyType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Layout(Header(), Trailer()));
        Assert.Contains("role 'data'", ex.Message, StringComparison.Ordinal);
    }
}
