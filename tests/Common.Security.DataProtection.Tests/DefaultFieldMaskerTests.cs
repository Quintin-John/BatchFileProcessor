using Common.Messaging.Contracts;

namespace Common.Security.DataProtection.Tests;

public sealed class DefaultFieldMaskerTests
{
    private static DataProtectionPolicy DefaultPolicy() => new(new Dictionary<string, FieldProtection>
    {
        ["pan"] = new(ProtectionAction.Encrypt, "first6last4", RedactInLogs: true),
        ["amount"] = new(ProtectionAction.Clear, null, RedactInLogs: false),
    });

    private static DefaultFieldMasker Masker(DataProtectionPolicy? policy = null) =>
        new(policy ?? DefaultPolicy(), new IMasker[] { new PanMasker() });

    private static FieldProtectionContext Ctx(string field) => new("file-abc", 101, field);

    [Fact]
    public void Mask_WithStrategy_MasksValue()
    {
        Assert.Equal("123456******3456", Masker().Mask(Ctx("pan"), new ClearFieldValue("1234567890123456")));
    }

    [Fact]
    public void Mask_WithoutStrategy_ReturnsClearString()
    {
        Assert.Equal("221.73", Masker().Mask(Ctx("amount"), new ClearFieldValue(221.73m)));
    }

    [Fact]
    public void Mask_EncryptedValue_Throws()
    {
        var encrypted = new EncryptedFieldValue(
            new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        Assert.Throws<InvalidOperationException>(() => Masker().Mask(Ctx("pan"), encrypted));
    }

    [Fact]
    public void Mask_UnknownStrategy_Throws()
    {
        var policy = new DataProtectionPolicy(new Dictionary<string, FieldProtection>
        {
            ["x"] = new(ProtectionAction.Clear, "ghost-strategy", RedactInLogs: false),
        });

        Assert.Throws<InvalidOperationException>(() => Masker(policy).Mask(Ctx("x"), new ClearFieldValue("value")));
    }

    [Fact]
    public void Mask_UnclassifiedField_ThrowsFailClosed()
    {
        Assert.Throws<KeyNotFoundException>(() => Masker().Mask(Ctx("unknown"), new ClearFieldValue("x")));
    }

    [Fact]
    public void Constructor_WithNullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultFieldMasker(null!, new IMasker[] { new PanMasker() }));
        Assert.Throws<ArgumentNullException>(() => new DefaultFieldMasker(DefaultPolicy(), null!));
    }
}
