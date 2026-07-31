using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class LayoutLoaderTests
{
    private const string ValidYaml = """
        version: "1.0"
        recordLength: 10
        encoding: ascii
        discriminator: { start: 1, length: 2 }
        recordTypes:
          head:
            match: "HD"
            fields:
              - { name: rectype, start: 1, length: 2 }
              - { name: acct, start: 3, length: 8, encrypt: true, required: true }
        """;

    [Fact]
    public void Load_ValidLayout_ProducesModel_WithFlags()
    {
        var layout = LayoutLoader.Load(ValidYaml);

        Assert.Equal("1.0", layout.Version);
        Assert.Equal(10, layout.RecordLength);
        Assert.Equal(1, layout.DiscriminatorStart);
        Assert.Equal(2, layout.DiscriminatorLength);

        var head = layout.ResolveByDiscriminator("HD")!;
        Assert.Equal("head", head.Name);
        Assert.Equal(2, head.Fields.Count);
        Assert.False(head.Fields[0].Encrypt);   // absent flags default to false
        Assert.False(head.Fields[0].Required);
        Assert.True(head.Fields[1].Encrypt);     // encrypt/required read from the layout
        Assert.True(head.Fields[1].Required);
    }

    [Fact]
    public void Load_IgnoresUnknownFieldKeys()
    {
        // 'type' (and any other unmodelled key) is not the pump's concern — it must be ignored, not fail.
        const string yaml = """
            version: "1"
            recordLength: 4
            encoding: ascii
            discriminator: { start: 1, length: 2 }
            recordTypes:
              r:
                match: "M"
                fields: [ { name: f, start: 1, length: 4, type: whatever } ]
            """;

        var layout = LayoutLoader.Load(yaml);

        Assert.Single(layout.ResolveByDiscriminator("M")!.Fields);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_BlankYaml_Throws(string? yaml) =>
        Assert.ThrowsAny<ArgumentException>(() => LayoutLoader.Load(yaml!));

    [Fact]
    public void Load_MalformedYaml_Throws() =>
        Assert.Throws<FormatException>(() => LayoutLoader.Load("recordTypes: {oops"));

    [Fact]
    public void Load_NoDiscriminator_Throws() =>
        Assert.Throws<FormatException>(() => LayoutLoader.Load("version: \"1\"\nrecordLength: 4\nrecordTypes: {}"));

    [Fact]
    public void Load_NoRecordTypes_Throws() =>
        Assert.Throws<FormatException>(() => LayoutLoader.Load("discriminator: { start: 1, length: 2 }\nrecordLength: 4"));

    [Fact]
    public void Load_NonContiguousFields_Throws()
    {
        const string yaml = """
            version: "1"
            recordLength: 10
            encoding: ascii
            discriminator: { start: 1, length: 2 }
            recordTypes:
              r:
                match: "M"
                fields:
                  - { name: a, start: 1, length: 2 }
                  - { name: b, start: 3, length: 7 }
            """;

        Assert.Throws<FormatException>(() => LayoutLoader.Load(yaml)); // covers 1-9 of 10; fields must tile
    }

    [Fact]
    public void Load_DuplicateMatch_Throws()
    {
        const string yaml = """
            version: "1"
            recordLength: 4
            encoding: ascii
            discriminator: { start: 1, length: 2 }
            recordTypes:
              a:
                match: "M"
                fields: [ { name: f, start: 1, length: 4, type: string } ]
              b:
                match: "M"
                fields: [ { name: f, start: 1, length: 4, type: string } ]
            """;

        Assert.Throws<FormatException>(() => LayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_RecordMissingMatch_Throws()
    {
        const string yaml = """
            recordLength: 4
            discriminator: { start: 1, length: 2 }
            recordTypes:
              r:
                fields: [ { name: f, start: 1, length: 4, type: string } ]
            """;

        Assert.Throws<FormatException>(() => LayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_RecordWithoutFields_Throws()
    {
        const string yaml = """
            recordLength: 4
            discriminator: { start: 1, length: 2 }
            recordTypes:
              r:
                match: "M"
            """;

        Assert.Throws<FormatException>(() => LayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_FieldWithoutName_Throws()
    {
        const string yaml = """
            recordLength: 4
            discriminator: { start: 1, length: 2 }
            recordTypes:
              r:
                match: "M"
                fields: [ { start: 1, length: 4, type: string } ]
            """;

        Assert.Throws<FormatException>(() => LayoutLoader.Load(yaml));
    }

    [Fact]
    public void LoadFromFile_BlankPath_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => LayoutLoader.LoadFromFile("  "));

    [Fact]
    public void Load_RealG266V48Layout_ValidatesThroughGenericModel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Layouts", "g266-v4.8.yaml");

        var layout = LayoutLoader.LoadFromFile(path);

        Assert.Equal("4.8", layout.Version);
        Assert.Equal(1200, layout.RecordLength);
        Assert.Equal(4, layout.RecordTypes.Count);
        Assert.Equal("per", layout.ResolveByDiscriminator("TRAN")!.Name);
        Assert.Equal("aer", layout.ResolveByDiscriminator("AUTH")!.Name);
    }
}
