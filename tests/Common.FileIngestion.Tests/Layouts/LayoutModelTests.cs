using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class LayoutModelTests
{
    private static FieldDefinition Field(int start, int length, FieldType type = FieldType.Text) =>
        new($"f{start}", start, length, type);

    private static RecordDefinition Record(string name, string match, params FieldDefinition[] fields) =>
        new(name, match, fields);

    private static Layout ValidLayout() =>
        new("1.0", 10, "ascii", 1, 2, new[]
        {
            Record("head", "HD", Field(1, 2), Field(3, 8, FieldType.Number)),
            Record("detail", "DT", Field(1, 2), Field(3, 8, FieldType.Filler)),
        });

    // ---- FieldDefinition ----

    [Fact]
    public void FieldDefinition_ComputesOffsetAndEnd()
    {
        var field = new FieldDefinition("amount", 141, 17, FieldType.Number);

        Assert.Equal(140, field.Offset);
        Assert.Equal(157, field.EndInclusive);
    }

    [Theory]
    [InlineData(null, 1, 1)]
    [InlineData("", 1, 1)]
    [InlineData("f", 0, 1)]
    [InlineData("f", 1, 0)]
    public void FieldDefinition_InvalidArguments_Throw(string? name, int start, int length)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FieldDefinition(name!, start, length, FieldType.Text));
    }

    // ---- RecordDefinition ----

    [Fact]
    public void RecordDefinition_DefensivelyCopiesFields()
    {
        var fields = new List<FieldDefinition> { Field(1, 4) };
        var record = new RecordDefinition("r", "M", fields);

        fields.Add(Field(5, 4));

        Assert.Single(record.Fields);
    }

    [Theory]
    [InlineData(null, "M")]
    [InlineData("r", "")]
    public void RecordDefinition_BlankName_Throws(string? name, string? match)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RecordDefinition(name!, match!, new[] { Field(1, 4) }));
    }

    [Fact]
    public void RecordDefinition_EmptyFields_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RecordDefinition("r", "M", Array.Empty<FieldDefinition>()));
    }

    [Fact]
    public void RecordDefinition_NullFieldElement_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RecordDefinition("r", "M", new FieldDefinition[] { null! }));
    }

    // ---- Layout ----

    [Fact]
    public void Layout_ResolveByDiscriminator_FindsRecordOrReturnsNull()
    {
        var layout = ValidLayout();

        Assert.Equal("head", layout.ResolveByDiscriminator("HD")!.Name);
        Assert.Null(layout.ResolveByDiscriminator("XX"));
    }

    [Fact]
    public void Layout_ResolveByDiscriminator_BlankValue_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => ValidLayout().ResolveByDiscriminator(" "));
    }

    [Fact]
    public void Layout_RecordLengthBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Layout("1.0", 0, "ascii", 1, 2, new[] { Record("r", "M", Field(1, 1)) }));
    }

    [Fact]
    public void Layout_DiscriminatorExceedsRecord_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Layout("1.0", 10, "ascii", 9, 5, new[] { Record("r", "M", Field(1, 10)) }));
    }

    [Fact]
    public void Layout_EmptyRecordTypes_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new Layout("1.0", 10, "ascii", 1, 2, Array.Empty<RecordDefinition>()));
    }

    [Fact]
    public void Layout_DuplicateMatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Layout("1.0", 4, "ascii", 1, 2, new[]
        {
            Record("a", "M", Field(1, 4)),
            Record("b", "M", Field(1, 4)),
        }));
    }

    [Fact]
    public void Layout_FieldsWithGapOrOverlap_Throws()
    {
        // Fields 1-2 then 4-10 leaves a gap at position 3.
        Assert.Throws<ArgumentException>(() => new Layout("1.0", 10, "ascii", 1, 2, new[]
        {
            Record("r", "M", Field(1, 2), new FieldDefinition("f", 4, 7, FieldType.Filler)),
        }));
    }

    [Fact]
    public void Layout_FieldsDoNotCoverRecord_Throws()
    {
        // Fields cover only 1-9 of a 10-byte record.
        Assert.Throws<ArgumentException>(() => new Layout("1.0", 10, "ascii", 1, 2, new[]
        {
            Record("r", "M", Field(1, 2), Field(3, 7)),
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Layout_BlankVersionOrEncoding_Throws(string blank)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new Layout(blank, 4, "ascii", 1, 2, new[] { Record("r", "M", Field(1, 4)) }));
        Assert.ThrowsAny<ArgumentException>(
            () => new Layout("1.0", 4, blank, 1, 2, new[] { Record("r", "M", Field(1, 4)) }));
    }
}
