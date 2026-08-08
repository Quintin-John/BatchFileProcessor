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

    // Two record/row types, the same field names and encrypt flags expressed in each framing.
    private static Layout FixedWidth() => new(Version, 10, Encoding, 0, 1, 2, new[]
    {
        new RecordDefinition("hd", "HD", new[]
        {
            new FieldDefinition("rectype", 1, 2),
            new FieldDefinition("pan", 3, 8, encrypt: true),
        }),
        new RecordDefinition("dt", "DT", new[]
        {
            new FieldDefinition("rectype", 1, 2),
            new FieldDefinition("amount", 3, 8),
        }),
    });

    private static DelimitedLayout Delimited() => new(Version, ',', Encoding, new[]
    {
        new DelimitedRowDefinition("head", RowRole.Header, 1, new[]
        {
            new DelimitedFieldDefinition("rectype", 0),
            new DelimitedFieldDefinition("pan", 1, encrypt: true),
        }),
        new DelimitedRowDefinition("body", RowRole.Data, 0, new[]
        {
            new DelimitedFieldDefinition("rectype", 0),
            new DelimitedFieldDefinition("amount", 1),
        }),
    });

    private static void AssertSharedSurface(ILayout layout)
    {
        Assert.Equal(Version, layout.Version);
        Assert.Equal(Encoding, layout.Encoding);

        // Every field of every type, in declaration order, carrying its encrypt classification.
        Assert.Equal(["rectype", "pan", "rectype", "amount"], layout.DeclaredFields.Select(f => f.Name));
        Assert.Equal(["pan"], layout.DeclaredFields.Where(f => f.Encrypt).Select(f => f.Name));

        // "rectype" appears in both types. Collapsing it here would hide a conflicting classification from
        // the consumer that has to fail closed on one.
        Assert.Equal(2, layout.DeclaredFields.Count(f => f.Name == "rectype"));
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
        var layout = new DelimitedLayout(Version, ',', Encoding, new[]
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
