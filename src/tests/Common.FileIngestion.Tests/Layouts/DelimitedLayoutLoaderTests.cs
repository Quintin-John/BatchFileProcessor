using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class DelimitedLayoutLoaderTests
{
    private const string MinimalYaml = """
        version: "1.0"
        delimiter: ","
        encoding: ascii
        rowTypes:
          data:
            role: data
            fields:
              - { name: a, index: 0 }
              - { name: b, index: 1 }
        """;

    [Fact]
    public void Load_MinimalLayout_MapsEverything()
    {
        var layout = DelimitedLayoutLoader.Load(MinimalYaml);

        Assert.Equal("1.0", layout.Version);
        Assert.Equal(',', layout.Delimiter);
        Assert.Equal("ascii", layout.Encoding);
        Assert.Equal(RowRole.Data, layout.Data.Role);
        Assert.Equal(["a", "b"], layout.Data.Fields.Select(f => f.Name));
        Assert.Equal([0, 1], layout.Data.Fields.Select(f => f.Index));
    }

    [Fact]
    public void Load_HeaderAndTrailer_AreMappedWithRowCountsAndSkip()
    {
        const string yaml = """
            version: "2.0"
            delimiter: tab
            encoding: ascii
            rowTypes:
              head:
                role: header
                rows: 2
                skip: true
              body:
                role: data
                fields:
                  - { name: a, index: 0 }
              foot:
                role: trailer
                rows: 1
                skip: false
                fields:
                  - { name: recordCount, index: 0, required: true }
            """;

        var layout = DelimitedLayoutLoader.Load(yaml);

        Assert.Equal('\t', layout.Delimiter);
        Assert.Equal(2, layout.HeaderRows);
        Assert.Equal(1, layout.TrailerRows);
        Assert.True(layout.Header!.Skip);
        Assert.Empty(layout.Header.Fields);

        // A trailer with real values is mapped and emitted, not discarded.
        Assert.False(layout.Trailer!.Skip);
        Assert.Equal("recordCount", Assert.Single(layout.Trailer.Fields).Name);
    }

    [Fact]
    public void Load_FieldFlags_AreCarriedThrough()
    {
        const string yaml = """
            version: "1.0"
            delimiter: "|"
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { name: plain,   index: 0 }
                  - { name: secret,  index: 1, encrypt: true }
                  - { name: must,    index: 2, required: true }
                  - { name: ignored, index: 3, skip: true }
            """;

        var fields = DelimitedLayoutLoader.Load(yaml).Data.Fields;

        Assert.False(fields[0].Encrypt);
        Assert.True(fields[1].Encrypt);
        Assert.True(fields[2].Required);
        Assert.True(fields[3].Skip);
    }

    [Theory]
    [InlineData("tab", '\t')]
    [InlineData("space", ' ')]
    [InlineData("\",\"", ',')]
    [InlineData("\"|\"", '|')]
    [InlineData("\";\"", ';')]
    [InlineData("\"~\"", '~')]
    [InlineData("\"\\\\x1F\"", (char)0x1F)]
    public void Load_AnyDelimiter_IsAcceptedWithoutCodeChange(string token, char expected)
    {
        var yaml = $$"""
            version: "1.0"
            delimiter: {{token}}
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { name: a, index: 0 }
            """;

        Assert.Equal(expected, DelimitedLayoutLoader.Load(yaml).Delimiter);
    }

    [Fact]
    public void Load_RoleIsCaseInsensitive()
    {
        const string yaml = """
            version: "1.0"
            delimiter: ","
            encoding: ascii
            rowTypes:
              data:
                role: DATA
                fields:
                  - { name: a, index: 0 }
            """;

        Assert.Equal(RowRole.Data, DelimitedLayoutLoader.Load(yaml).Data.Role);
    }

    // ---------- fail-closed ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_BlankYaml_Throws(string? yaml)
    {
        Assert.ThrowsAny<ArgumentException>(() => DelimitedLayoutLoader.Load(yaml!));
    }

    [Fact]
    public void Load_MalformedYaml_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load("version: \"1.0\"\n  bad: [indent"));
    }

    [Fact]
    public void Load_NoRowTypes_Throws()
    {
        var ex = Assert.Throws<FormatException>(
            () => DelimitedLayoutLoader.Load("version: \"1.0\"\ndelimiter: \",\"\nencoding: ascii"));
        Assert.Contains("at least one row type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingDelimiter_Throws()
    {
        const string yaml = """
            version: "1.0"
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { name: a, index: 0 }
            """;

        var ex = Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
        Assert.Contains("delimiter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_UnresolvableDelimiter_Throws()
    {
        var yaml = MinimalYaml.Replace("delimiter: \",\"", "delimiter: comma", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_MissingRole_Throws()
    {
        const string yaml = """
            version: "1.0"
            delimiter: ","
            encoding: ascii
            rowTypes:
              data:
                fields:
                  - { name: a, index: 0 }
            """;

        var ex = Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
        Assert.Contains("must declare a role", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_UnknownRole_Throws()
    {
        var yaml = MinimalYaml.Replace("role: data", "role: footer", StringComparison.Ordinal);

        var ex = Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
        Assert.Contains("unknown role", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NonSkippedRowWithoutFields_Throws()
    {
        const string yaml = """
            version: "1.0"
            delimiter: ","
            encoding: ascii
            rowTypes:
              data:
                role: data
            """;

        var ex = Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
        Assert.Contains("must define fields", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_FieldWithoutName_Throws()
    {
        const string yaml = """
            version: "1.0"
            delimiter: ","
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { index: 0 }
            """;

        var ex = Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
        Assert.Contains("without a name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_FieldIndexGap_Throws()
    {
        var yaml = MinimalYaml.Replace("{ name: b, index: 1 }", "{ name: b, index: 2 }", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_SkipCombinedWithRequired_Throws()
    {
        var yaml = MinimalYaml.Replace(
            "{ name: b, index: 1 }", "{ name: b, index: 1, skip: true, required: true }", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_DataRowDeclaringRowCount_Throws()
    {
        var yaml = MinimalYaml.Replace("role: data", "role: data\n    rows: 1", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_HeaderWithoutRowCount_Throws()
    {
        const string yaml = """
            version: "1.0"
            delimiter: ","
            encoding: ascii
            rowTypes:
              head:
                role: header
                skip: true
              data:
                role: data
                fields:
                  - { name: a, index: 0 }
            """;

        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
    }

    // ---------- row match ----------

    [Fact]
    public void Load_TrailerMatch_IsMapped()
    {
        const string yaml = """
            version: "1.0"
            delimiter: tab
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { name: a, index: 0 }
              foot:
                role: trailer
                rows: 1
                skip: true
                match: { index: 2, value: Footer }
            """;

        var match = DelimitedLayoutLoader.Load(yaml).Trailer!.Match;

        // The marker column is declared, never assumed to be the first field.
        Assert.Equal(2, match!.Index);
        Assert.Equal("Footer", match.Value);
    }

    [Fact]
    public void Load_MatchWithoutIndex_DefaultsToTheFirstField()
    {
        const string yaml = """
            version: "1.0"
            delimiter: tab
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { name: a, index: 0 }
              foot:
                role: trailer
                rows: 1
                skip: true
                match: { value: Footer }
            """;

        Assert.Equal(0, DelimitedLayoutLoader.Load(yaml).Trailer!.Match!.Index);
    }

    [Fact]
    public void Load_MatchWithoutValue_Throws()
    {
        const string yaml = """
            version: "1.0"
            delimiter: tab
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { name: a, index: 0 }
              foot:
                role: trailer
                rows: 1
                skip: true
                match: { index: 0 }
            """;

        var ex = Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
        Assert.Contains("must declare the value", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MatchOnADataRow_Throws()
    {
        var yaml = MinimalYaml.Replace("role: data", "role: data\n    match: { index: 0, value: X }", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
    }

    [Fact]
    public void Load_MatchWithNegativeIndex_Throws()
    {
        const string yaml = """
            version: "1.0"
            delimiter: tab
            encoding: ascii
            rowTypes:
              data:
                role: data
                fields:
                  - { name: a, index: 0 }
              foot:
                role: trailer
                rows: 1
                skip: true
                match: { index: -1, value: Footer }
            """;

        Assert.Throws<FormatException>(() => DelimitedLayoutLoader.Load(yaml));
    }

    [Fact]
    public void LoadFromFile_BlankPath_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => DelimitedLayoutLoader.LoadFromFile("  "));

    // ---------- the real production layout ----------

    [Fact]
    public void LoadFromFile_RealForceUpdateBalanceLayout_IsAccepted()
    {
        // The generic loader must accept the actual transcribed spec, not just synthetic fixtures.
        var path = Path.Combine(AppContext.BaseDirectory, "Layouts", "force-update-balance-v1.0.yaml");

        var layout = DelimitedLayoutLoader.LoadFromFile(path);

        Assert.Equal("1.0", layout.Version);
        Assert.Equal('\t', layout.Delimiter);
        Assert.Equal("ascii", layout.Encoding);

        // One skipped header row, no trailer, and a data row of 16 contiguous fields.
        Assert.Equal(1, layout.HeaderRows);
        Assert.True(layout.Header!.Skip);
        Assert.Null(layout.Trailer);
        Assert.Equal(16, layout.Data.Fields.Count);
        Assert.Equal(
            Enumerable.Range(0, layout.Data.Fields.Count),
            layout.Data.Fields.Select(f => f.Index));

        // The three PCI-adjacent identifiers are the encrypted set; everything else travels clear.
        Assert.Equal(
            ["ACIExternalID", "ACICardExternalID", "AccountIdentifier"],
            layout.Data.Fields.Where(f => f.Encrypt).Select(f => f.Name));

        // GDAccountKey is the only field carrying neither flag.
        Assert.Equal(
            ["GDAccountKey"],
            layout.Data.Fields.Where(f => !f.Encrypt && !f.Required).Select(f => f.Name));
    }
}
