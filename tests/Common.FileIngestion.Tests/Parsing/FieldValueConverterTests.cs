using Common.FileIngestion.Layouts;
using Common.FileIngestion.Parsing;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Parsing;

public sealed class FieldValueConverterTests
{
    

    private static FieldDefinition Field(FieldType type, int scale = 0, string? format = null) =>
        new("f", 5, 10, type, scale, format); // start 5 => offset 4

    [Fact]
    public void Text_TrimsTrailingPadding()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Text), "hello     ");

        Assert.True(result.IsSuccess);
        Assert.Equal(new ClearFieldValue("hello"), result.Value);
    }

    [Fact]
    public void Number_WithoutScale_ParsesInteger()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Number), "0000000043");

        Assert.Equal(new ClearFieldValue(43m), result.Value);
    }

    [Fact]
    public void Number_WithScale_AppliesImpliedDecimal()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Number, scale: 2), "0000022173");

        Assert.Equal(new ClearFieldValue(221.73m), result.Value);
    }

    [Fact]
    public void Number_Negative_Parses()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Number), "-42");

        Assert.Equal(new ClearFieldValue(-42m), result.Value);
    }

    [Fact]
    public void Number_Invalid_RejectsWithLocation()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Number), "12A4");

        Assert.False(result.IsSuccess);
        Assert.Equal("f", result.Reason!.Field);
        Assert.Equal("NON_NUMERIC", result.Reason.Code);
        Assert.Equal(4, result.Reason.Offset);
        Assert.Equal(10, result.Reason.Length);
    }

    [Fact]
    public void Date_ValidDefaultFormat_Succeeds()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Date), "2022-11-07");

        Assert.Equal(new ClearFieldValue("2022-11-07"), result.Value);
    }

    [Fact]
    public void Date_CustomFormat_Succeeds()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Date, format: "yyyyMMdd"), "20221107");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Date_Invalid_Rejects()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Date), "2022-13-99");

        Assert.False(result.IsSuccess);
        Assert.Equal("BAD_DATE", result.Reason!.Code);
    }

    [Fact]
    public void Time_Valid_Succeeds()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Time), "12:40:00");

        Assert.Equal(new ClearFieldValue("12:40:00"), result.Value);
    }

    [Fact]
    public void Time_Invalid_Rejects()
    {
        var result = FieldValueConverter.Convert(Field(FieldType.Time), "99:99:99");

        Assert.False(result.IsSuccess);
        Assert.Equal("BAD_TIME", result.Reason!.Code);
    }

    [Fact]
    public void Filler_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => FieldValueConverter.Convert(Field(FieldType.Filler), "x"));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => FieldValueConverter.Convert(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => FieldValueConverter.Convert(Field(FieldType.Text), null!));
    }
}
