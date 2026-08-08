using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

/// <summary>
/// The shared <see cref="ILayout"/> surface, asserted identically against both framings — the point of the
/// abstraction is that a consumer cannot tell them apart.
/// </summary>
public sealed class DeclaredFieldsTests
{
    private const string Version = "1.0";
    private const string Encoding = "ascii";

    // Named for the flags they carry; the layout is the only thing that says what a field means.
    private const string MarkerField = "marker";
    private const string SecretField = "secret";
    private const string PlainField = "plain";

    // Two record/row types, the same field names and encrypt flags expressed in each framing.
    private static Layout FixedWidth() => new(Version, 10, Encoding, 0, 1, 2, new[]
    {
        new RecordDefinition("hd", "HD", new[]
        {
            new FieldDefinition(MarkerField, 1, 2),
            new FieldDefinition(SecretField, 3, 8, encrypt: true),
        }),
        new RecordDefinition("dt", "DT", new[]
        {
            new FieldDefinition(MarkerField, 1, 2),
            new FieldDefinition(PlainField, 3, 8),
        }),
    });

    private static DelimitedLayout Delimited() => new(Version, ",", '\n', Encoding, new[]
    {
        new DelimitedRowDefinition("head", RowRole.Header, 1, new[]
        {
            new DelimitedFieldDefinition(MarkerField, 0),
            new DelimitedFieldDefinition(SecretField, 1, encrypt: true),
        }),
        new DelimitedRowDefinition("body", RowRole.Data, 0, new[]
        {
            new DelimitedFieldDefinition(MarkerField, 0),
            new DelimitedFieldDefinition(PlainField, 1),
        }),
    });

    private static void AssertSharedSurface(ILayout layout)
    {
        Assert.Equal(Version, layout.Version);
        Assert.Equal(Encoding, layout.Encoding);

        // Every field of every type, in declaration order, carrying its encrypt flag.
        Assert.Equal(
            [MarkerField, SecretField, MarkerField, PlainField],
            layout.DeclaredFields.Select(f => f.Name));
        Assert.Equal([SecretField], layout.DeclaredFields.Where(f => f.Encrypt).Select(f => f.Name));

        // The marker field appears in both types. Collapsing it here would hide a name flagged one way in
        // one type and another way elsewhere from the consumer that has to fail closed on it.
        Assert.Equal(2, layout.DeclaredFields.Count(f => f.Name == MarkerField));
    }

    [Fact]
    public void FixedWidthLayout_SatisfiesTheSharedSurface() => AssertSharedSurface(FixedWidth());

    [Fact]
    public void DelimitedLayout_SatisfiesTheSharedSurface() => AssertSharedSurface(Delimited());

    [Fact]
    public void BothFramings_ProduceIdenticalSharedSurfaces()
    {
        // Same declaration expressed two ways must be indistinguishable through ILayout.
        Assert.Equal(FixedWidth().DeclaredFields, Delimited().DeclaredFields);
    }

    [Fact]
    public void DeclaredFields_OfASkippedRowType_ContributesNothing()
    {
        // A skipped type declares no fields, so it adds nothing to classify.
        var layout = new DelimitedLayout(Version, ",", '\n', Encoding, new[]
        {
            new DelimitedRowDefinition("head", RowRole.Header, 1, [], skip: true),
            new DelimitedRowDefinition("body", RowRole.Data, 0, [new DelimitedFieldDefinition("a", 0)]),
        });

        Assert.Equal(["a"], layout.DeclaredFields.Select(f => f.Name));
    }

    [Fact]
    public void DeclaredFields_OfASkippedRecordType_ContributesNothing()
    {
        var layout = new Layout(Version, 10, Encoding, 0, 1, 2, new[]
        {
            new RecordDefinition("hd", "HD", [], skip: true),
            new RecordDefinition("dt", "DT", [new FieldDefinition("a", 1, 10)]),
        });

        Assert.Equal(["a"], layout.DeclaredFields.Select(f => f.Name));
    }
}
