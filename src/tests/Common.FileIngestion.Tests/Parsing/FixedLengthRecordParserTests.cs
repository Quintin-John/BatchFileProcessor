using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Parsing;

public sealed class FixedLengthRecordParserTests
{
    // Two record types, each tiling a 10-char record. Fields are named for the flags they carry, so the
    // fixture states what the parser must do and nothing about what a field might hold.
    private const string MarkerField = "marker";
    private const string SecretField = "secret";
    private const string PlainField = "plain";
    private const string OptionalField = "optional";

    private static Layout Layout() => new("1.0", 10, "ascii", 0, 1, 2, new[]
    {
        new RecordDefinition("hd", "HD", new[]
        {
            new FieldDefinition(MarkerField, 1, 2),
            new FieldDefinition(SecretField, 3, 4, encrypt: true, required: true),
            new FieldDefinition(PlainField, 7, 4),
        }),
        new RecordDefinition("dt", "DT", new[]
        {
            new FieldDefinition(MarkerField, 1, 2),
            new FieldDefinition(OptionalField, 3, 8),
        }),
    });

    // One record of the "hd" type: marker, then the required+encrypted field, then the plain one. Values
    // are arbitrary and distinct so a mis-tiled field shows up in the assertion.
    private const string ValidRecord = "HDAAAABBBB";

    private static FixedLengthRecordParser Parser() => new(Layout());

    // The reader is the authority on a record's extent. This fixture layout declares no terminator, so a
    // record occupies exactly its content bytes — derived from the content, never a literal.
    private static FramedRecord Framed(long recordSeq, long byteOffset, string content) =>
        new(recordSeq, byteOffset, content.Length, content);

    [Fact]
    public void Parse_ValidRecord_SlicesEveryFieldRaw()
    {
        var result = Parser().Parse(Framed(7, 60, ValidRecord));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Record!.Locator.RecordSeq);
        Assert.Equal(60, result.Record.Locator.ByteOffset);
        Assert.Equal("HD", result.Record.Locator.RecordType);
        Assert.Equal(3, result.Record.Fields.Count); // every field emitted; nothing skipped or interpreted
        Assert.Equal(new ClearFieldValue("HD"), result.Record.Fields[MarkerField]);
        Assert.Equal(new ClearFieldValue("AAAA"), result.Record.Fields[SecretField]); // raw, not encrypted here
        Assert.Equal(new ClearFieldValue("BBBB"), result.Record.Fields[PlainField]);
    }

    [Fact]
    public void Parse_PreservesSpacesVerbatim_DoesNotTrimOrInterpret()
    {
        // The plain field is all spaces (accepted); the required one has trailing spaces but a non-blank
        // value, which satisfies required.
        var result = Parser().Parse(Framed(1, 0, "HDAB      "));

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue("AB  "), result.Record!.Fields[SecretField]); // spaces kept, not trimmed
        Assert.Equal(new ClearFieldValue("    "), result.Record.Fields[PlainField]);   // empty value accepted
    }

    [Fact]
    public void Parse_RequiredFieldBlank_Rejects()
    {
        var result = Parser().Parse(Framed(1, 0, "HD    BBBB")); // the required field is blank

        Assert.False(result.IsSuccess);
        Assert.Equal("HD", result.RecordType);
        Assert.Equal("HD    BBBB", result.RawRecord);
        var reason = Assert.Single(result.Reasons!);
        Assert.Equal(SecretField, reason.Field);
        Assert.Equal("REQUIRED_MISSING", reason.Code);
    }

    [Fact]
    public void Parse_OptionalFieldBlank_IsAccepted()
    {
        var result = Parser().Parse(Framed(1, 0, "DT        ")); // blank, and not required

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue("        "), result.Record!.Fields[OptionalField]);
    }

    [Fact]
    public void Parse_SkipField_TiledForCoverage_ButNotEmitted()
    {
        var layout = new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
        {
            new RecordDefinition("r", "MX", new[]
            {
                new FieldDefinition(MarkerField, 1, 2),
                new FieldDefinition("data", 3, 4),
                new FieldDefinition("filler", 7, 4, skip: true),
            }),
        });

        var result = new FixedLengthRecordParser(layout).Parse(Framed(1, 0, "MXDATA9999"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Record!.Fields.Count);              // filler is tiled but not emitted
        Assert.Equal(new ClearFieldValue("DATA"), result.Record.Fields["data"]);
        Assert.False(result.Record.Fields.ContainsKey("filler"));  // skipped from the upstream message
    }

    [Fact]
    public void Parse_SkipRecordType_ReturnsSkipped_NotSuccessOrReject()
    {
        var layout = new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
        {
            new RecordDefinition("dt", "DT", new[] { new FieldDefinition(MarkerField, 1, 2), new FieldDefinition("body", 3, 8) }),
            new RecordDefinition("hd", "HD", Array.Empty<FieldDefinition>(), skip: true),
        });

        var result = new FixedLengthRecordParser(layout).Parse(Framed(1, 0, "HD12345678"));

        Assert.True(result.IsSkipped);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Record);
        Assert.Equal("HD", result.RecordType);
    }

    [Fact]
    public void Parse_WrongLength_Rejects()
    {
        var result = Parser().Parse(Framed(1, 0, "HD1"));

        Assert.False(result.IsSuccess);
        Assert.Equal("WRONG_LENGTH", result.Reasons![0].Code);
    }

    [Fact]
    public void Parse_UnknownDiscriminator_Rejects()
    {
        var result = Parser().Parse(Framed(1, 0, "ZZ00000000"));

        Assert.False(result.IsSuccess);
        Assert.Equal("ZZ", result.RecordType);
        Assert.Equal("UNKNOWN_RECORD_TYPE", result.Reasons![0].Code);
        Assert.Equal("ZZ00000000", result.RawRecord);
    }

    [Fact]
    public void Parse_BlankDiscriminator_RejectsOneRecord_WithPlaceholder_DoesNotThrow()
    {
        var result = Parser().Parse(Framed(1, 0, "  00000000"));

        Assert.False(result.IsSuccess);
        Assert.Equal("?", result.RecordType); // non-blank placeholder so RecordLocator accepts it
        Assert.Equal("UNKNOWN_RECORD_TYPE", result.Reasons![0].Code);
    }

    [Fact]
    public void Constructor_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FixedLengthRecordParser(null!));
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, -1L)]
    public void Parse_OutOfRangePosition_Throws(long recordSeq, long byteOffset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Parser().Parse(Framed(recordSeq, byteOffset, ValidRecord)));
    }

    [Fact]
    public void Parse_ValidRecord_CarriesFramedExtentIntoLocator()
    {
        // The parser must not re-derive the extent from the layout: the reader owns it, so a resume point
        // stays correct when framing is variable-length.
        var framed = Framed(7, 60, ValidRecord);

        var result = Parser().Parse(framed);

        Assert.True(result.IsSuccess);
        Assert.Equal(framed.ByteLength, result.Record!.Locator.ByteLength);
        Assert.Equal(framed.ByteOffset + framed.ByteLength, result.Record.Locator.EndByteOffset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Parse_ByteLengthBelowOne_Throws(int byteLength)
    {
        const string content = ValidRecord;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Parser().Parse(new FramedRecord(1, 0, byteLength, content)));
    }

    [Fact]
    public void Parse_NullContent_Throws()
    {
        // default(FramedRecord) bypasses any construction guard, so the parser must fail closed on it.
        Assert.Throws<ArgumentNullException>(() => Parser().Parse(new FramedRecord(1, 0, 1, null!)));
    }
}
