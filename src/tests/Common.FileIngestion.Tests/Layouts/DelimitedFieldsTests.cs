using Common.FileIngestion.Layouts;

namespace Common.FileIngestion.Tests.Layouts;

public sealed class DelimitedFieldsTests
{
    private const char Delimiter = ',';

    private static List<string> ReadAll(string row, char delimiter = Delimiter)
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
    [InlineData('\t')]
    [InlineData('|')]
    [InlineData((char)0x1F)]
    public void SplitsOnWhicheverDelimiterIsGiven(char delimiter)
    {
        Assert.Equal(["a", "b"], ReadAll(string.Join(delimiter, "a", "b"), delimiter));
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
        const string row = "a,,c,";
        var sequential = ReadAll(row);

        for (var index = 0; index < sequential.Count; index++)
        {
            Assert.True(DelimitedFields.TryReadAt(row, index, Delimiter, out var value));
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
