using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class DelimitedFieldsTests
{
    private const string Delimiter = ",";

    private static List<string> ReadAll(string row, string delimiter = Delimiter)
    {
        var values = new List<string>();
        var fields = new DelimitedFields(row, delimiter);
        while (fields.TryReadNext(out var value))
        {
            values.Add(value.ToString());
        }

        return values;
    }

    [Fact]
    public void ReadsEveryFieldInOrder()
    {
        Assert.Equal(["a", "b", "c"], ReadAll("a,b,c"));
    }

    [Fact]
    public void AnEmptyRowIsOneEmptyField_NotZeroFields()
    {
        // A blank row still carries a value, and the field-count check has to see it as one — otherwise a
        // single-field layout would silently accept a blank line.
        Assert.Equal([string.Empty], ReadAll(string.Empty));
    }

    [Fact]
    public void ATrailingDelimiterYieldsAFinalEmptyField()
    {
        Assert.Equal(["a", "b", string.Empty], ReadAll("a,b,"));
    }

    [Fact]
    public void ALeadingDelimiterYieldsALeadingEmptyField()
    {
        Assert.Equal([string.Empty, "a"], ReadAll(",a"));
    }

    [Fact]
    public void ConsecutiveDelimitersYieldEmptyFieldsBetweenThem()
    {
        Assert.Equal(["a", string.Empty, string.Empty, "b"], ReadAll("a,,,b"));
    }

    [Fact]
    public void ValuesAreVerbatim_SpacesAreNeitherTrimmedNorInterpreted()
    {
        Assert.Equal(["  a  ", " b"], ReadAll("  a  , b"));
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("|")]
    [InlineData("\u001F")]
    [InlineData("~|~")]
    [InlineData("||")]
    [InlineData("<SEP>")]
    public void SplitsOnWhicheverDelimiterIsGiven(string delimiter)
    {
        // One character or several makes no difference to the rule; the delimiter is text.
        Assert.Equal(["a", "b"], ReadAll(string.Join(delimiter, "a", "b"), delimiter));
    }

    // ---------- a delimiter of more than one character ----------

    [Fact]
    public void AMultiCharacterDelimiterIsMatchedWhole_NotCharacterByCharacter()
    {
        // The failure this guards against: treating '~|~' as "any of ~ or |", which would read six fields
        // out of a two-field row.
        Assert.Equal(["a", "b"], ReadAll("a~|~b", "~|~"));
    }

    [Fact]
    public void ACharacterOfAMultiCharacterDelimiterIsOrdinaryContentOnItsOwn()
    {
        // A lone '~' is not a boundary when the boundary is '~|~', so it stays inside the field's value.
        Assert.Equal(["a~b", "c"], ReadAll("a~b~|~c", "~|~"));
    }

    [Fact]
    public void MatchesDoNotOverlap_ReadingResumesPastTheWholeDelimiter()
    {
        // With '||' over 'a|||b' the second and third bars overlap a candidate match. Consuming the whole
        // delimiter leaves '|b'; consuming one character would leave '|' as a phantom empty field.
        Assert.Equal(["a", "|b"], ReadAll("a|||b", "||"));
    }

    [Fact]
    public void APartialDelimiterAtTheEndIsContent_NotABoundary()
    {
        Assert.Equal(["a", "b~|"], ReadAll("a~|~b~|", "~|~"));
    }

    [Fact]
    public void AdjacentMultiCharacterDelimitersYieldAnEmptyFieldBetweenThem()
    {
        Assert.Equal(["a", string.Empty, "b"], ReadAll("a~|~~|~b", "~|~"));
    }

    [Fact]
    public void AnEmptyDelimiter_Throws()
    {
        // Guarded because an empty delimiter matches everywhere and scanning would never advance.
        Assert.Throws<ArgumentException>(() => ReadAll("a,b", string.Empty));
        Assert.Throws<ArgumentException>(() => DelimitedFields.Count("a,b", string.Empty));
        Assert.Throws<ArgumentException>(() => DelimitedFields.TryReadAt("a,b", 0, string.Empty, out _));
    }

    // ---------- Count agrees with what reading actually yields ----------

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a,b,c")]
    [InlineData("a,,b")]
    [InlineData(",")]
    [InlineData("a,b,")]
    public void Count_AlwaysMatchesTheNumberOfFieldsRead(string row)
    {
        // The parser rejects on Count and then reads that many fields. If the two ever disagreed it would
        // accept a row and then map it short, so they are asserted against each other rather than separately.
        Assert.Equal(ReadAll(row).Count, DelimitedFields.Count(row, Delimiter));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a~|~b~|~c")]
    [InlineData("a~|~~|~b")]
    [InlineData("a|||b")]
    [InlineData("a~b~|~c~")]
    public void Count_MatchesWhatIsRead_ForAMultiCharacterDelimiterToo(string row)
    {
        const string delimiter = "~|~";
        Assert.Equal(ReadAll(row, delimiter).Count, DelimitedFields.Count(row, delimiter));
    }

    // ---------- reading a single field by position ----------

    [Theory]
    [InlineData(0, "a")]
    [InlineData(1, "b")]
    [InlineData(2, "c")]
    public void TryReadAt_ReturnsTheFieldAtThatPosition(int index, string expected)
    {
        Assert.True(DelimitedFields.TryReadAt("a,b,c", index, Delimiter, out var value));
        Assert.Equal(expected, value.ToString());
    }

    [Fact]
    public void TryReadAt_AgreesWithReadingSequentially()
    {
        // The reader locates a marker by position while the parser reads sequentially; a disagreement would
        // mean the marker is verified against a different column than the value that is mapped.
        AssertPositionalReadingAgreesWithSequential("a,,c,", Delimiter);
    }

    [Fact]
    public void TryReadAt_AgreesWithReadingSequentially_ForAMultiCharacterDelimiterToo()
    {
        AssertPositionalReadingAgreesWithSequential("a~b~|~~|~c~|~", "~|~");
    }

    private static void AssertPositionalReadingAgreesWithSequential(string row, string delimiter)
    {
        var sequential = ReadAll(row, delimiter);

        for (var index = 0; index < sequential.Count; index++)
        {
            Assert.True(DelimitedFields.TryReadAt(row, index, delimiter, out var value));
            Assert.Equal(sequential[index], value.ToString());
        }
    }

    [Fact]
    public void TryReadAt_BeyondTheLastField_ReturnsFalse()
    {
        Assert.False(DelimitedFields.TryReadAt("a,b", 2, Delimiter, out var value));
        Assert.True(value.IsEmpty);
    }

    [Fact]
    public void TryReadAt_NegativeIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            DelimitedFields.TryReadAt("a,b", -1, Delimiter, out _);
        });
    }

    [Fact]
    public void TryReadNext_AfterExhaustion_KeepsReturningFalse()
    {
        var fields = new DelimitedFields("a", Delimiter);

        Assert.True(fields.TryReadNext(out _));
        Assert.False(fields.TryReadNext(out _));
        Assert.False(fields.TryReadNext(out _));
    }
}
