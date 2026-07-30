namespace Common.Security.DataProtection.Tests;

public sealed class DataProtectionPolicyLoaderTests
{
    private const string ValidYaml = """
        fields:
          pan:
            action: encrypt
            mask: first6last4
            redactInLogs: true
          amount:
            action: clear
        """;

    [Fact]
    public void Load_ValidPolicy_ParsesFields()
    {
        var policy = DataProtectionPolicyLoader.Load(ValidYaml);

        var pan = policy.GetProtection("pan");
        Assert.Equal(ProtectionAction.Encrypt, pan.Action);
        Assert.Equal("first6last4", pan.MaskStrategy);
        Assert.True(pan.RedactInLogs);

        var amount = policy.GetProtection("amount");
        Assert.Equal(ProtectionAction.Clear, amount.Action);
        Assert.Null(amount.MaskStrategy);
        Assert.False(amount.RedactInLogs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_BlankYaml_Throws(string? yaml)
    {
        Assert.ThrowsAny<ArgumentException>(() => DataProtectionPolicyLoader.Load(yaml!));
    }

    [Fact]
    public void Load_NoFieldsSection_Throws()
    {
        Assert.Throws<FormatException>(() => DataProtectionPolicyLoader.Load("somethingElse: 1"));
    }

    [Fact]
    public void Load_EmptyFields_Throws()
    {
        Assert.Throws<FormatException>(() => DataProtectionPolicyLoader.Load("fields: {}"));
    }

    [Fact]
    public void Load_UnknownAction_Throws()
    {
        const string yaml = """
            fields:
              x:
                action: shred
            """;

        Assert.Throws<FormatException>(() => DataProtectionPolicyLoader.Load(yaml));
    }

    [Fact]
    public void Load_MissingAction_Throws()
    {
        const string yaml = """
            fields:
              x:
                mask: first6last4
            """;

        Assert.Throws<FormatException>(() => DataProtectionPolicyLoader.Load(yaml));
    }

    [Fact]
    public void Load_MalformedYaml_Throws()
    {
        Assert.Throws<FormatException>(() => DataProtectionPolicyLoader.Load("fields: {unclosed"));
    }

    [Fact]
    public void LoadFromFile_ReadsAndParses()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dp-policy-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, ValidYaml);
        try
        {
            var policy = DataProtectionPolicyLoader.LoadFromFile(path);

            Assert.Equal(ProtectionAction.Encrypt, policy.GetProtection("pan").Action);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_BlankPath_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => DataProtectionPolicyLoader.LoadFromFile("  "));
    }
}
