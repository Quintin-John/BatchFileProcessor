using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Parsing;

public sealed class DelimitedRecordParserTests
{
    private const string Version = "1.0";
    private const string EncodingName = "ascii";
    private const string Comma = ",";
    private const string HeaderName = "head";
    private const string DataName = "body";

    // Fields are named for the flags they carry, so the fixture states what the parser must do and nothing
    // about what a field might hold. A skipped header type sits alongside.
    private const string MarkerField = "marker";
    private const string SecretField = "secret";
    private const string IgnoredField = "ignored";

    private static DelimitedLayout Layout(string delimiter = Comma) => new(Version, delimiter, '\n', EncodingName, new[]
    {
        new DelimitedRowDefinition(HeaderName, RowRole.Header, 1, [], skip: true),
        new DelimitedRowDefinition(DataName, RowRole.Data, 0, new[]
        {
            new DelimitedFieldDefinition(MarkerField, 0),
            new DelimitedFieldDefinition(SecretField, 1, encrypt: true, required: true),
            new DelimitedFieldDefinition(IgnoredField, 2, skip: true),
        }),
    });

    private static DelimitedRecordParser Parser(string delimiter = Comma) => new(Layout(delimiter));

    // The reader is the authority on both the extent and the row type; this mirrors what it emits.
    private static FramedRecord Framed(string content, string rowType = DataName, long seq = 1, long offset = 0) =>
        new(seq, offset, content.Length + 1, content, rowType);

    // ---------- happy path ----------

    [Fact]
    public void Parse_ValidRow_SplitsEveryFieldRaw()
    {
        var result = Parser().Parse(Framed("DT,AAAA,XX"));

        Assert.True(result.IsSuccess);
        Assert.Equal(DataName, result.Record!.Locator.RecordType);
        Assert.Equal(2, result.Record.Fields.Count); // the skipped field is counted but not emitted
        Assert.Equal(new ClearFieldValue("DT"), result.Record.Fields[MarkerField]);
        Assert.Equal(new ClearFieldValue("AAAA"), result.Record.Fields[SecretField]);
        Assert.False(result.Record.Fields.ContainsKey(IgnoredField));
    }

    [Fact]
    public void Parse_CarriesFramedPositionAndExtentIntoLocator()
    {
        var framed = Framed("DT,AAAA,XX", seq: 7, offset: 60);

        var locator = Parser().Parse(framed).Record!.Locator;

        Assert.Equal(framed.RecordSeq, locator.RecordSeq);
        Assert.Equal(framed.ByteOffset, locator.ByteOffset);
        Assert.Equal(framed.ByteLength, locator.ByteLength);
        Assert.Equal(framed.ByteOffset + framed.ByteLength, locator.EndByteOffset);
    }

    [Fact]
    public void Parse_PreservesSpacesVerbatim_DoesNotTrimOrInterpret()
    {
        var result = Parser().Parse(Framed("  DT  , AC ,XX"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue("  DT  "), result.Record!.Fields[MarkerField]);
        Assert.Equal(new ClearFieldValue(" AC "), result.Record.Fields[SecretField]);
    }

    [Fact]
    public void Parse_EmptyOptionalField_IsAccepted()
    {
        var result = Parser().Parse(Framed(",AAAA,XX")); // the marker field is empty, and not required

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue(string.Empty), result.Record!.Fields[MarkerField]);
    }

    [Fact]
    public void Parse_SkippedRowType_ReturnsSkipped_NotSuccessOrReject()
    {
        var result = Parser().Parse(Framed("col1,col2", rowType: HeaderName));

        Assert.True(result.IsSkipped);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Record);
        Assert.Equal(HeaderName, result.RecordType);
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("|")]
    [InlineData(";")]
    [InlineData("~")]
    [InlineData("\u001F")]
    [InlineData("~|~")]
    [InlineData("||")]
    public void Parse_AnyDelimiter_SplitsTheSameWay(string delimiter)
    {
        // The delimiter is layout data, so a new one needs no code change here either.
        var row = string.Join(delimiter, "DT", "AAAA", "XX");

        var result = Parser(delimiter).Parse(Framed(row));

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue("AAAA"), result.Record!.Fields[SecretField]);
    }

    [Fact]
    public void Parse_NonSkippedTrailer_IsMappedLikeAnyRow()
    {
        // A trailer carrying a control total is emitted, not discarded — the layout decides, not the parser.
        const string trailerName = "foot";
        var layout = new DelimitedLayout(Version, Comma, '\n', EncodingName, new[]
        {
            new DelimitedRowDefinition(DataName, RowRole.Data, 0, [new DelimitedFieldDefinition("a", 0)]),
            new DelimitedRowDefinition(trailerName, RowRole.Trailer, 1, new[]
            {
                new DelimitedFieldDefinition("label", 0),
                new DelimitedFieldDefinition("recordCount", 1, required: true),
            }),
        });

        var result = new DelimitedRecordParser(layout).Parse(Framed("COUNT,42", rowType: trailerName));

        Assert.True(result.IsSuccess);
        Assert.Equal(trailerName, result.Record!.Locator.RecordType);
        Assert.Equal(new ClearFieldValue("42"), result.Record.Fields["recordCount"]);
    }

    // ---------- rejections ----------

    [Fact]
    public void Parse_RequiredFieldBlank_Rejects()
    {
        var result = Parser().Parse(Framed("DT,   ,XX"));

        Assert.False(result.IsSuccess);
        Assert.Equal(DataName, result.RecordType);
        Assert.Equal("DT,   ,XX", result.RawRecord);
        var reason = Assert.Single(result.Reasons!);
        Assert.Equal(SecretField, reason.Field);
        Assert.Equal("REQUIRED_MISSING", reason.Code);

        // Offset and length are byte concepts; a delimited field has neither.
        Assert.Null(reason.Offset);
        Assert.Null(reason.Length);
    }

    [Theory]
    [InlineData("DT,AAAA")]                 // too few
    [InlineData("DT,AAAA,XX,EXTRA")]        // too many
    [InlineData("")]                        // a blank row splits into one value
    public void Parse_WrongFieldCount_Rejects(string row)
    {
        var result = Parser().Parse(Framed(row));

        Assert.False(result.IsSuccess);
        Assert.Equal("WRONG_FIELD_COUNT", Assert.Single(result.Reasons!).Code);
    }

    [Fact]
    public void Parse_WrongFieldCount_ReportsExpectedAndActual()
    {
        var result = Parser().Parse(Framed("DT,AAAA"));

        var reason = Assert.Single(result.Reasons!);
        Assert.Equal("3", reason.Expected);
        Assert.Equal("2", reason.Actual);
    }

    [Fact]
    public void Parse_QuotedDelimiterInsideAValue_FailsClosedRatherThanMisMapping()
    {
        // RFC 4180 quoting is not implemented. A quoted delimiter splits into an extra value, and the field
        // count check rejects the row instead of silently shifting every field after it by one.
        var result = Parser().Parse(Framed("DT,\"AC,CT\",XX"));

        Assert.False(result.IsSuccess);
        Assert.Equal("WRONG_FIELD_COUNT", Assert.Single(result.Reasons!).Code);
    }

    [Fact]
    public void Parse_UnknownRowType_Rejects()
    {
        var result = Parser().Parse(Framed("DT,AAAA,XX", rowType: "no-such-type"));

        Assert.False(result.IsSuccess);
        Assert.Equal("no-such-type", result.RecordType);
        Assert.Equal("UNKNOWN_ROW_TYPE", Assert.Single(result.Reasons!).Code);
    }

    // ---------- fail-closed on wiring, not data ----------

    [Fact]
    public void Parse_UntaggedRecord_Throws()
    {
        // An untagged record means this parser was paired with a reader that does not classify rows. That is
        // a wiring fault affecting every row, so it must fault the run rather than quarantine one record.
        var untagged = new FramedRecord(1, 0, 5, "DT,AAAA,XX");

        Assert.Throws<ArgumentException>(() => Parser().Parse(untagged));
    }

    [Fact]
    public void Constructor_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DelimitedRecordParser(null!));
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, -1L)]
    public void Parse_OutOfRangePosition_Throws(long recordSeq, long byteOffset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Parser().Parse(Framed("DT,AAAA,XX", seq: recordSeq, offset: byteOffset)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Parse_ByteLengthBelowOne_Throws(int byteLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Parser().Parse(new FramedRecord(1, 0, byteLength, "DT,AAAA,XX", DataName)));
    }

    [Fact]
    public void Parse_NullContent_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Parser().Parse(new FramedRecord(1, 0, 1, null!, DataName)));
    }
}
