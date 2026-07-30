using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Parsing;

public sealed class FixedLengthRecordParserTests
{
    private static Layout Layout() => new("1.0", 10, "ascii", 1, 2, new[]
    {
        new RecordDefinition("hd", "HD", new[]
        {
            new FieldDefinition("rectype", 1, 2, FieldType.Text),
            new FieldDefinition("amount", 3, 8, FieldType.Number, scale: 2),
        }),
        new RecordDefinition("dt", "DT", new[]
        {
            new FieldDefinition("rectype", 1, 2, FieldType.Text),
            new FieldDefinition("filler", 3, 8, FieldType.Filler),
        }),
    });

    private static FixedLengthRecordParser Parser() => new(Layout());

    [Fact]
    public void Parse_ValidRecord_MapsFields()
    {
        var result = Parser().Parse(7, 60, "HD00022173".AsSpan());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Record!.Locator.RecordSeq);
        Assert.Equal(60, result.Record.Locator.ByteOffset);
        Assert.Equal("HD", result.Record.Locator.RecordType);
        Assert.Equal(new ClearFieldValue("HD"), result.Record.Fields["rectype"]);
        Assert.Equal(new ClearFieldValue(221.73m), result.Record.Fields["amount"]);
    }

    [Fact]
    public void Parse_SkipsFillerFields()
    {
        var result = Parser().Parse(1, 0, "DTignored ".AsSpan());

        Assert.True(result.IsSuccess);
        Assert.Single(result.Record!.Fields); // only rectype, filler skipped
        Assert.False(result.Record.Fields.ContainsKey("filler"));
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
    public void Parse_BlankDiscriminator_RejectsOneRecord_DoesNotThrow()
    {
        // BUG-2: a blank record-type field must reject this one record, not throw and fault the file.
        var result = Parser().Parse(1, 0, "  00000000".AsSpan());

        Assert.False(result.IsSuccess);
        Assert.Equal("?", result.RecordType); // non-blank placeholder so RecordLocator accepts it
        Assert.Equal("UNKNOWN_RECORD_TYPE", result.Reasons![0].Code);
        Assert.Equal("  00000000", result.RawRecord);
    }

    [Fact]
    public void Parse_WrongLength_Rejects()
    {
        var result = Parser().Parse(1, 0, "HD1".AsSpan());

        Assert.False(result.IsSuccess);
        Assert.Equal("WRONG_LENGTH", result.Reasons![0].Code);
    }

    [Fact]
    public void Parse_InvalidField_RejectsWithReasons()
    {
        var result = Parser().Parse(1, 0, "HD00ABCD00".AsSpan()); // amount not numeric

        Assert.False(result.IsSuccess);
        Assert.Equal("HD", result.RecordType);
        Assert.Contains(result.Reasons!, r => r is { Field: "amount", Code: "NON_NUMERIC" });
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
        Assert.Throws<ArgumentOutOfRangeException>(() => Parser().Parse(recordSeq, byteOffset, "HD00022173".AsSpan()));
    }
}
