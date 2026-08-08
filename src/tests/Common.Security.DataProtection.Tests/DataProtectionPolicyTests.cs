namespace Common.Security.DataProtection.Tests;

public sealed class DataProtectionPolicyTests
{
    // Field names state only what the policy does with them; nothing here knows what a field holds.
    private const string EncryptedField = "encrypted";
    private const string ClearField = "clear";

    private static DataProtectionPolicy Policy() => new(new Dictionary<string, ProtectionAction>
    {
        [EncryptedField] = ProtectionAction.Encrypt,
        [ClearField] = ProtectionAction.Clear,
    });

    [Theory]
    [InlineData(EncryptedField, ProtectionAction.Encrypt)]
    [InlineData(ClearField, ProtectionAction.Clear)]
    public void GetProtection_ForAClassifiedField_ReturnsItsAction(string field, ProtectionAction expected)
    {
        Assert.Equal(expected, Policy().GetProtection(field));
    }

    [Fact]
    public void GetProtection_ForAnUnclassifiedField_ThrowsFailClosed()
    {
        // Returning a default here would hand back Clear, which is the leak this is guarding.
        Assert.Throws<KeyNotFoundException>(() => Policy().GetProtection("unclassified"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetProtection_ForABlankFieldName_Throws(string? field)
    {
        Assert.ThrowsAny<ArgumentException>(() => Policy().GetProtection(field!));
    }

    [Fact]
    public void Constructor_WithNullFields_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DataProtectionPolicy(null!));
    }

    [Fact]
    public void Fields_AreDefensivelyCopied()
    {
        var source = new Dictionary<string, ProtectionAction> { [ClearField] = ProtectionAction.Clear };
        var policy = new DataProtectionPolicy(source);

        source[EncryptedField] = ProtectionAction.Encrypt;

        Assert.Single(policy.Fields);
    }
}
