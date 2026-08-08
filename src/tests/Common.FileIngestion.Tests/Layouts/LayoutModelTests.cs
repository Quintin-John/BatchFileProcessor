using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class LayoutModelTests
{
    private static FieldDefinition Field(int start, int length) => new($"f{start}", start, length);

    private static RecordDefinition Record(string name, string match, params FieldDefinition[] fields) =>
        new(name, match, fields);

    private static Layout ValidLayout() =>
        new("1.0", 10, "ascii", 0, 1, 2, new[]
        {
            Record("head", "HD", Field(1, 2), Field(3, 8)),
            Record("detail", "DT", Field(1, 2), Field(3, 8)),
        });

    // ---- FieldDefinition ----

    [Fact]
    public void FieldDefinition_ComputesOffsetAndEnd_FlagsDefaultToFalse()
    {
        var field = new FieldDefinition("amount", 141, 17);

        Assert.Equal(140, field.Offset);
        Assert.Equal(157, field.EndInclusive);
        Assert.False(field.Encrypt);
        Assert.False(field.Required);
        Assert.False(field.Skip);
    }

    [Fact]
    public void FieldDefinition_CarriesEncryptAndRequiredFlags()
    {
        var field = new FieldDefinition("secret", 1, 16, encrypt: true, required: true);

        Assert.True(field.Encrypt);
        Assert.True(field.Required);
    }

    [Fact]
    public void FieldDefinition_CarriesSkipFlag()
    {
        var field = new FieldDefinition("filler", 1, 8, skip: true);

        Assert.True(field.Skip);
        Assert.False(field.Encrypt);
        Assert.False(field.Required);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FieldDefinition_SkipCombinedWithEncryptOrRequired_Throws(bool encrypt, bool required)
    {
        // A field that is never emitted cannot also be encrypted or required.
        Assert.Throws<ArgumentException>(() => new FieldDefinition("f", 1, 8, encrypt, required, skip: true));
    }

    [Theory]
    [InlineData(null, 1, 1)]
    [InlineData("", 1, 1)]
    [InlineData("f", 0, 1)]
    [InlineData("f", 1, 0)]
    public void FieldDefinition_InvalidArguments_Throw(string? name, int start, int length)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FieldDefinition(name!, start, length));
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
    public void RecordDefinition_Skip_AllowsNoFields()
    {
        var record = new RecordDefinition("ftr", "TRAI", Array.Empty<FieldDefinition>(), skip: true);

        Assert.True(record.Skip);
        Assert.Empty(record.Fields);
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

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Layout_ResolveByDiscriminator_BlankValue_ReturnsNull(string blank)
    {
        // Blank is data (an empty type field), not a caller bug — unknown, so null (not a throw).
        Assert.Null(ValidLayout().ResolveByDiscriminator(blank));
    }

    [Fact]
    public void Layout_ResolveByDiscriminator_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ValidLayout().ResolveByDiscriminator(null!));
    }

    [Fact]
    public void Layout_RecordLengthBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Layout("1.0", 0, "ascii", 0, 1, 2, new[] { Record("r", "M", Field(1, 1)) }));
    }

    [Fact]
    public void Layout_DiscriminatorExceedsRecord_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Layout("1.0", 10, "ascii", 0, 9, 5, new[] { Record("r", "M", Field(1, 10)) }));
    }

    [Fact]
    public void Layout_StoresTerminatorLength()
    {
        var layout = new Layout("1.0", 10, "ascii", 2, 1, 2, new[] { Record("r", "M", Field(1, 10)) });

        Assert.Equal(2, layout.TerminatorLength);
    }

    [Fact]
    public void Layout_NegativeTerminator_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Layout("1.0", 10, "ascii", -1, 1, 2, new[] { Record("r", "M", Field(1, 10)) }));
    }

    [Fact]
    public void Layout_EmptyRecordTypes_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new Layout("1.0", 10, "ascii", 0, 1, 2, Array.Empty<RecordDefinition>()));
    }

    [Fact]
    public void Layout_DuplicateMatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Layout("1.0", 4, "ascii", 0, 1, 2, new[]
        {
            Record("a", "M", Field(1, 4)),
            Record("b", "M", Field(1, 4)),
        }));
    }

    [Fact]
    public void Layout_FieldsWithGapOrOverlap_Throws()
    {
        // Fields 1-2 then 4-10 leaves a gap at position 3.
        Assert.Throws<ArgumentException>(() => new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
        {
            Record("r", "M", Field(1, 2), new FieldDefinition("f", 4, 7)),
        }));
    }

    [Fact]
    public void Layout_FieldsDoNotCoverRecord_Throws()
    {
        // Fields cover only 1-9 of a 10-byte record.
        Assert.Throws<ArgumentException>(() => new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
        {
            Record("r", "M", Field(1, 2), Field(3, 7)),
        }));
    }

    [Fact]
    public void Layout_SkipRecord_IsExemptFromCoverage()
    {
        // An emitted record must tile; a skip record (no fields) is consumed for framing and coverage-exempt.
        var layout = new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
        {
            Record("detail", "DT", Field(1, 2), Field(3, 8)),
            new RecordDefinition("trailer", "TR", Array.Empty<FieldDefinition>(), skip: true),
        });

        Assert.True(layout.ResolveByDiscriminator("TR")!.Skip);
        Assert.False(layout.ResolveByDiscriminator("DT")!.Skip);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Layout_BlankVersionOrEncoding_Throws(string blank)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new Layout(blank, 4, "ascii", 0, 1, 2, new[] { Record("r", "M", Field(1, 4)) }));
        Assert.ThrowsAny<ArgumentException>(
            () => new Layout("1.0", 4, blank, 0, 1, 2, new[] { Record("r", "M", Field(1, 4)) }));
    }
}
