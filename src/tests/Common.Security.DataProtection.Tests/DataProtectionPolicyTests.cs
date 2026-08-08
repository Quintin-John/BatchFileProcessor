namespace Common.Security.DataProtection.Tests;

public sealed class DataProtectionPolicyTests
{
    private static DataProtectionPolicy Policy() => new(new Dictionary<string, FieldProtection>
    {
        ["pan"] = new(ProtectionAction.Encrypt, "first6last4", RedactInLogs: true),
        ["amount"] = new(ProtectionAction.Clear, null, RedactInLogs: false),
    });

    [Fact]
    public void GetProtection_ForClassifiedField_ReturnsIt()
    {
        var protection = Policy().GetProtection("pan");

        Assert.Equal(ProtectionAction.Encrypt, protection.Action);
        Assert.Equal("first6last4", protection.MaskStrategy);
        Assert.True(protection.RedactInLogs);
    }

    [Fact]
    public void GetProtection_ForUnclassifiedField_ThrowsFailClosed()
    {
        Assert.Throws<KeyNotFoundException>(() => Policy().GetProtection("unknown"));
    }

    [Fact]
    public void TryGetProtection_ReflectsClassification()
    {
        var policy = Policy();

        Assert.True(policy.TryGetProtection("amount", out var found));
        Assert.Equal(ProtectionAction.Clear, found!.Action);
        Assert.False(policy.TryGetProtection("nope", out _));
    }

    [Fact]
    public void Constructor_WithNullFields_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DataProtectionPolicy(null!));
    }

    [Fact]
    public void Fields_AreDefensivelyCopied()
    {
        var source = new Dictionary<string, FieldProtection>
        {
            ["a"] = new(ProtectionAction.Clear, null, false),
        };
        var policy = new DataProtectionPolicy(source);

        source["b"] = new(ProtectionAction.Encrypt, null, false);

        Assert.Single(policy.Fields);
    }
}
