using Common.FileIngestion.Parsing;
using Common.Messaging.Contracts;

namespace Common.FileIngestion.Tests.Parsing;

public sealed class FieldConversionTests
{
    [Fact]
    public void Success_HoldsValue_NoReason()
    {
        var value = new ClearFieldValue("x");
        var conversion = FieldConversion.Success(value);

        Assert.True(conversion.IsSuccess);
        Assert.Same(value, conversion.Value);
        Assert.Null(conversion.Reason);
    }

    [Fact]
    public void Rejected_HoldsReason_NoValue()
    {
        var reason = new RejectReason("f", "rule", "CODE");
        var conversion = FieldConversion.Rejected(reason);

        Assert.False(conversion.IsSuccess);
        Assert.Same(reason, conversion.Reason);
        Assert.Null(conversion.Value);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => FieldConversion.Success(null!));
        Assert.Throws<ArgumentNullException>(() => FieldConversion.Rejected(null!));
    }
}
