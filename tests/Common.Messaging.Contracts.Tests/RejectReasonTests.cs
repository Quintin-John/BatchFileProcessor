namespace Common.Messaging.Contracts.Tests;

public sealed class RejectReasonTests
{
    [Fact]
    public void Constructor_WithAllArguments_SetsProperties()
    {
        var reason = new RejectReason(
            field: "amount",
            rule: "decimal",
            code: "NON_NUMERIC",
            expected: "numeric(17,2)",
            actual: "  ABC",
            offset: 84,
            length: 17);

        Assert.Equal("amount", reason.Field);
        Assert.Equal("decimal", reason.Rule);
        Assert.Equal("NON_NUMERIC", reason.Code);
        Assert.Equal("numeric(17,2)", reason.Expected);
        Assert.Equal("  ABC", reason.Actual);
        Assert.Equal(84, reason.Offset);
        Assert.Equal(17, reason.Length);
    }

    [Fact]
    public void Constructor_WithOnlyRequiredArguments_LeavesOptionalsNull()
    {
        var reason = new RejectReason("field", "rule", "CODE");

        Assert.Null(reason.Expected);
        Assert.Null(reason.Actual);
        Assert.Null(reason.Offset);
        Assert.Null(reason.Length);
    }

    [Theory]
    [InlineData(null, "rule", "CODE")]
    [InlineData("", "rule", "CODE")]
    [InlineData("  ", "rule", "CODE")]
    [InlineData("field", null, "CODE")]
    [InlineData("field", "", "CODE")]
    [InlineData("field", "rule", null)]
    [InlineData("field", "rule", "  ")]
    public void Constructor_WithBlankRequiredArgument_Throws(string? field, string? rule, string? code)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RejectReason(field!, rule!, code!));
    }

    [Fact]
    public void Constructor_WithNegativeOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RejectReason("f", "r", "C", offset: -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_WithNonPositiveLength_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RejectReason("f", "r", "C", length: length));
    }

    [Fact]
    public void Constructor_WithZeroOffset_IsAllowed()
    {
        var reason = new RejectReason("f", "r", "C", offset: 0, length: 1);

        Assert.Equal(0, reason.Offset);
        Assert.Equal(1, reason.Length);
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = new RejectReason("amount", "decimal", "NON_NUMERIC", offset: 84, length: 17);
        var b = new RejectReason("amount", "decimal", "NON_NUMERIC", offset: 84, length: 17);
        var c = new RejectReason("amount", "decimal", "OUT_OF_RANGE", offset: 84, length: 17);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
