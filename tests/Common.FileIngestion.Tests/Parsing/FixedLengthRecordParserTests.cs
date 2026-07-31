using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Parsing;

public sealed class FixedLengthRecordParserTests
{
    // Two record types, each tiling a 10-char record. 'acct' is encrypted + required.
    private static Layout Layout() => new("1.0", 10, "ascii", 0, 1, 2, new[]
    {
        new RecordDefinition("hd", "HD", new[]
        {
            new FieldDefinition("rectype", 1, 2),
            new FieldDefinition("acct", 3, 4, encrypt: true, required: true),
            new FieldDefinition("rest", 7, 4),
        }),
        new RecordDefinition("dt", "DT", new[]
        {
            new FieldDefinition("rectype", 1, 2),
            new FieldDefinition("pad", 3, 8),
        }),
    });

    private static FixedLengthRecordParser Parser() => new(Layout());

    [Fact]
    public void Parse_ValidRecord_SlicesEveryFieldRaw()
    {
        var result = Parser().Parse(7, 60, "HDACCT1234".AsSpan());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Record!.Locator.RecordSeq);
        Assert.Equal(60, result.Record.Locator.ByteOffset);
        Assert.Equal("HD", result.Record.Locator.RecordType);
        Assert.Equal(3, result.Record.Fields.Count); // every field emitted; nothing skipped or interpreted
        Assert.Equal(new ClearFieldValue("HD"), result.Record.Fields["rectype"]);
        Assert.Equal(new ClearFieldValue("ACCT"), result.Record.Fields["acct"]);   // raw, not encrypted here
        Assert.Equal(new ClearFieldValue("1234"), result.Record.Fields["rest"]);
    }

    [Fact]
    public void Parse_PreservesSpacesVerbatim_DoesNotTrimOrInterpret()
    {
        // 'rest' is all spaces (accepted); 'acct' has trailing spaces but a non-blank value (required is satisfied).
        var result = Parser().Parse(1, 0, "HDAB      ".AsSpan());

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue("AB  "), result.Record!.Fields["acct"]); // spaces kept, not trimmed
        Assert.Equal(new ClearFieldValue("    "), result.Record.Fields["rest"]);  // empty value accepted
    }

    [Fact]
    public void Parse_RequiredFieldBlank_Rejects()
    {
        var result = Parser().Parse(1, 0, "HD    1234".AsSpan()); // acct is blank

        Assert.False(result.IsSuccess);
        Assert.Equal("HD", result.RecordType);
        Assert.Equal("HD    1234", result.RawRecord);
        var reason = Assert.Single(result.Reasons!);
        Assert.Equal("acct", reason.Field);
        Assert.Equal("REQUIRED_MISSING", reason.Code);
    }

    [Fact]
    public void Parse_OptionalFieldBlank_IsAccepted()
    {
        var result = Parser().Parse(1, 0, "DT        ".AsSpan()); // pad blank, not required

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue("        "), result.Record!.Fields["pad"]);
    }

    [Fact]
    public void Parse_SkipField_TiledForCoverage_ButNotEmitted()
    {
        var layout = new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
        {
            new RecordDefinition("r", "MX", new[]
            {
                new FieldDefinition("rectype", 1, 2),
                new FieldDefinition("data", 3, 4),
                new FieldDefinition("filler", 7, 4, skip: true),
            }),
        });

        var result = new FixedLengthRecordParser(layout).Parse(1, 0, "MXDATA9999".AsSpan());

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
            new RecordDefinition("dt", "DT", new[] { new FieldDefinition("rectype", 1, 2), new FieldDefinition("body", 3, 8) }),
            new RecordDefinition("hd", "HD", Array.Empty<FieldDefinition>(), skip: true),
        });

        var result = new FixedLengthRecordParser(layout).Parse(1, 0, "HD12345678".AsSpan());

        Assert.True(result.IsSkipped);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Record);
        Assert.Equal("HD", result.RecordType);
    }

    [Fact]
    public void Parse_WrongLength_Rejects()
    {
        var result = Parser().Parse(1, 0, "HD1".AsSpan());

        Assert.False(result.IsSuccess);
        Assert.Equal("WRONG_LENGTH", result.Reasons![0].Code);
    }

    [Fact]
    public void Parse_UnknownDiscriminator_Rejects()
    {
        var result = Parser().Parse(1, 0, "ZZ00000000".AsSpan());

        Assert.False(result.IsSuccess);
        Assert.Equal("ZZ", result.RecordType);
        Assert.Equal("UNKNOWN_RECORD_TYPE", result.Reasons![0].Code);
        Assert.Equal("ZZ00000000", result.RawRecord);
    }

    [Fact]
    public void Parse_BlankDiscriminator_RejectsOneRecord_WithPlaceholder_DoesNotThrow()
    {
        var result = Parser().Parse(1, 0, "  00000000".AsSpan());

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
        Assert.Throws<ArgumentOutOfRangeException>(() => Parser().Parse(recordSeq, byteOffset, "HDACCT1234".AsSpan()));
    }
}
