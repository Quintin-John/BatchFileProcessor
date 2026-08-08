using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class DelimitedRowDefinitionTests
{
    private static DelimitedFieldDefinition[] Fields(int count) =>
        Enumerable.Range(0, count).Select(i => new DelimitedFieldDefinition($"f{i}", i)).ToArray();

    // ---------- field definitions ----------

    [Fact]
    public void Field_WithValidArguments_SetsProperties()
    {
        var field = new DelimitedFieldDefinition("acct", 3, encrypt: true, required: true);

        Assert.Equal("acct", field.Name);
        Assert.Equal(3, field.Index);
        Assert.True(field.Encrypt);
        Assert.True(field.Required);
        Assert.False(field.Skip);
    }

    [Fact]
    public void Field_IndexZero_IsAllowed()
    {
        // Delimited indexes are 0-based, unlike fixed-width 1-based byte starts.
        Assert.Equal(0, new DelimitedFieldDefinition("first", 0).Index);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Field_BlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DelimitedFieldDefinition(name!, 0));
    }

    [Fact]
    public void Field_NegativeIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelimitedFieldDefinition("f", -1));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Field_SkipCombinedWithEncryptOrRequired_Throws(bool encrypt, bool required)
    {
        // A field that is never emitted cannot be encrypted or required — the same invariant the fixed-width
        // field definition enforces.
        Assert.Throws<ArgumentException>(
            () => new DelimitedFieldDefinition("f", 0, encrypt, required, skip: true));
    }

    // ---------- row definitions ----------

    [Fact]
    public void Row_WithValidArguments_SetsProperties()
    {
        const int fieldCount = 4;
        var row = new DelimitedRowDefinition("data", RowRole.Data, rows: 0, Fields(fieldCount));

        Assert.Equal("data", row.Name);
        Assert.Equal(RowRole.Data, row.Role);
        Assert.Equal(0, row.Rows);
        Assert.False(row.Skip);
        Assert.Equal(fieldCount, row.Fields.Count);
    }

    [Fact]
    public void Row_SkippedRowType_MayOmitFields()
    {
        // The delimited counterpart of a skipped fixed-width record: consumed for framing, never sliced.
        var row = new DelimitedRowDefinition("header", RowRole.Header, rows: 1, [], skip: true);

        Assert.True(row.Skip);
        Assert.Empty(row.Fields);
    }

    [Fact]
    public void Row_TrailerWithFields_IsEmittable()
    {
        // A trailer carrying a control total is declared and emitted, not discarded — skip is a per-row-type
        // decision in the layout, not an assumption in code.
        var row = new DelimitedRowDefinition("trailer", RowRole.Trailer, rows: 1, Fields(2), skip: false);

        Assert.False(row.Skip);
        Assert.Equal(2, row.Fields.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Row_BlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new DelimitedRowDefinition(name!, RowRole.Data, 0, Fields(1)));
    }

    [Fact]
    public void Row_NullFields_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DelimitedRowDefinition("r", RowRole.Data, 0, null!));
    }

    [Fact]
    public void Row_NullFieldElement_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new DelimitedRowDefinition("r", RowRole.Data, 0, new DelimitedFieldDefinition[] { null! }));
    }

    [Fact]
    public void Row_NotSkippedWithNoFields_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DelimitedRowDefinition("r", RowRole.Data, 0, []));
    }

    [Fact]
    public void Row_UnknownRole_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DelimitedRowDefinition("r", (RowRole)99, 0, Fields(1)));
    }

    [Fact]
    public void Row_DataDeclaringRowCount_Throws()
    {
        // Data spans whatever the header and trailer leave; a declared count would be a second source of
        // truth that could contradict the file.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DelimitedRowDefinition("data", RowRole.Data, rows: 1, Fields(1)));
        Assert.Contains("must not declare a row count", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RowRole.Header, 0)]
    [InlineData(RowRole.Header, -1)]
    [InlineData(RowRole.Trailer, 0)]
    [InlineData(RowRole.Trailer, -1)]
    public void Row_PositionalRoleWithoutPositiveRowCount_Throws(RowRole role, int rows)
    {
        // A positional row type must say how many rows it claims, or nothing could be assigned to it.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DelimitedRowDefinition("r", role, rows, Fields(1)));
    }

    [Fact]
    public void Row_FieldIndexGap_Throws()
    {
        var fields = new[] { new DelimitedFieldDefinition("a", 0), new DelimitedFieldDefinition("c", 2) };

        var ex = Assert.Throws<ArgumentException>(
            () => new DelimitedRowDefinition("r", RowRole.Data, 0, fields));
        Assert.Contains("expected 1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_FieldIndexOutOfOrder_Throws()
    {
        var fields = new[] { new DelimitedFieldDefinition("b", 1), new DelimitedFieldDefinition("a", 0) };

        Assert.Throws<ArgumentException>(() => new DelimitedRowDefinition("r", RowRole.Data, 0, fields));
    }

    [Fact]
    public void Row_DuplicateFieldIndex_Throws()
    {
        var fields = new[] { new DelimitedFieldDefinition("a", 0), new DelimitedFieldDefinition("b", 0) };

        Assert.Throws<ArgumentException>(() => new DelimitedRowDefinition("r", RowRole.Data, 0, fields));
    }

    [Fact]
    public void Row_DuplicateFieldName_Throws()
    {
        var fields = new[] { new DelimitedFieldDefinition("same", 0), new DelimitedFieldDefinition("same", 1) };

        var ex = Assert.Throws<ArgumentException>(
            () => new DelimitedRowDefinition("r", RowRole.Data, 0, fields));
        Assert.Contains("duplicate field name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- row match ----------

    [Fact]
    public void Match_WithValidArguments_SetsProperties()
    {
        var match = new RowMatch(3, "Footer");

        Assert.Equal(3, match.Index);
        Assert.Equal("Footer", match.Value);
    }

    [Fact]
    public void Match_IndexZero_IsAllowed() => Assert.Equal(0, new RowMatch(0, "Footer").Index);

    [Fact]
    public void Match_NegativeIndex_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RowMatch(-1, "Footer"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Match_BlankValue_Throws(string? value)
    {
        // A blank marker could not distinguish anything, so it would silently disable the check.
        Assert.ThrowsAny<ArgumentException>(() => new RowMatch(0, value!));
    }

    [Theory]
    [InlineData(RowRole.Header)]
    [InlineData(RowRole.Trailer)]
    public void Row_PositionalRoleWithMatch_IsAllowed(RowRole role)
    {
        var row = new DelimitedRowDefinition("r", role, 1, Fields(1), skip: true, new RowMatch(0, "Footer"));

        Assert.Equal("Footer", row.Match!.Value);
    }

    [Fact]
    public void Row_WithoutMatch_LeavesItNull() =>
        Assert.Null(new DelimitedRowDefinition("r", RowRole.Trailer, 1, Fields(1)).Match);

    [Fact]
    public void Row_DataRoleWithMatch_IsAccepted()
    {
        // On a data row type the marker is what identifies it, so several body types can share one file.
        var row = new DelimitedRowDefinition("r", RowRole.Data, 0, Fields(1), skip: false, new RowMatch(0, "X"));

        Assert.Equal("X", row.Match!.Value);
    }

    [Fact]
    public void Row_MatchBeyondItsOwnDeclaredFields_Throws()
    {
        // The type maps two columns but claims its marker sits in a third, so no row of that type could ever
        // be checked against it.
        var ex = Assert.Throws<ArgumentException>(
            () => new DelimitedRowDefinition("r", RowRole.Data, 0, Fields(2), skip: false, new RowMatch(2, "X")));
        Assert.Contains("declares only 2 field(s)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_SkippedTypeMayCarryAMarkerBeyondItsFields()
    {
        // A skipped type maps nothing, so the column carrying its marker is simply one it does not name; the
        // physical row still has it.
        var row = new DelimitedRowDefinition("r", RowRole.Trailer, 1, [], skip: true, new RowMatch(4, "END"));

        Assert.Equal(4, row.Match!.Index);
    }

    [Fact]
    public void Row_Fields_AreDefensivelyCopied()
    {
        var source = new List<DelimitedFieldDefinition> { new("a", 0) };
        var row = new DelimitedRowDefinition("r", RowRole.Data, 0, source);

        source.Add(new DelimitedFieldDefinition("b", 1));

        Assert.Single(row.Fields);
    }
}
