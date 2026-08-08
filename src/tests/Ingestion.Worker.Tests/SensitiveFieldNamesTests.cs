using Common.FileIngestion.Layouts;

namespace Ingestion.Worker.Tests;

public sealed class SensitiveFieldNamesTests
{
    private static Layout Layout(params RecordDefinition[] records) => new("1.0", 10, "ascii", 0, 1, 2, records);

    [Fact]
    public void From_CollectsOnlyEncryptFields_ExcludingClearRequiredAndSkip()
    {
        var layout = Layout(new RecordDefinition("r", "MM", new[]
        {
            new FieldDefinition("plain", 1, 2),
            new FieldDefinition("secret", 3, 2, encrypt: true),
            new FieldDefinition("req", 5, 2, required: true),
            new FieldDefinition("filler", 7, 4, skip: true),
        }));

        Assert.Equal("secret", Assert.Single(SensitiveFieldNames.From(new[] { layout })));
    }

    [Fact]
    public void From_UnionsAcrossRecordTypesAndLayouts()
    {
        var first = Layout(
            new RecordDefinition("a", "AA", new[] { new FieldDefinition("t", 1, 2), new FieldDefinition("x", 3, 8, encrypt: true) }),
            new RecordDefinition("b", "BB", new[] { new FieldDefinition("t", 1, 2), new FieldDefinition("y", 3, 8, encrypt: true) }));
        var second = Layout(
            new RecordDefinition("c", "CC", new[] { new FieldDefinition("t", 1, 2), new FieldDefinition("z", 3, 8, encrypt: true) }));

        var names = SensitiveFieldNames.From(new[] { first, second });

        Assert.Equal(3, names.Count);
        Assert.Contains("x", names);
        Assert.Contains("y", names);
        Assert.Contains("z", names);
    }

    [Fact]
    public void From_NoEncryptFields_ReturnsEmpty()
    {
        var layout = Layout(new RecordDefinition("r", "MM", new[]
        {
            new FieldDefinition("a", 1, 5),
            new FieldDefinition("b", 6, 5),
        }));

        Assert.Empty(SensitiveFieldNames.From(new[] { layout }));
    }

    [Fact]
    public void From_NullLayouts_Throws() =>
        Assert.Throws<ArgumentNullException>(() => SensitiveFieldNames.From(null!));

    [Fact]
    public void From_NullLayoutElement_Throws() =>
        Assert.Throws<ArgumentNullException>(() => SensitiveFieldNames.From(new Layout[] { null! }));

    [Fact]
    public void From_UnionsAcrossMixedFramings()
    {
        // Redaction keys come from every profile's layout regardless of framing — a delimited profile's
        // PCI-adjacent fields must be redacted in logs just as a fixed-width profile's are.
        var fixedWidth = Layout(new RecordDefinition("r", "MM", new[]
        {
            new FieldDefinition("plain", 1, 2),
            new FieldDefinition("pan", 3, 8, encrypt: true),
        }));

        var delimited = new DelimitedLayout("1.0", '\t', "ascii", new[]
        {
            new DelimitedRowDefinition("body", RowRole.Data, 0, new[]
            {
                new DelimitedFieldDefinition("plain", 0),
                new DelimitedFieldDefinition("accountIdentifier", 1, encrypt: true),
            }),
        });

        var names = SensitiveFieldNames.From(new ILayout[] { fixedWidth, delimited });

        Assert.Equal(2, names.Count);
        Assert.Contains("pan", names);
        Assert.Contains("accountIdentifier", names);
        Assert.DoesNotContain("plain", names);
    }
}
