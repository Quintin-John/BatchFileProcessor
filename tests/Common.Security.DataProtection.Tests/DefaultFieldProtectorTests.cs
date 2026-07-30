using System.Security.Cryptography;
using Common.Messaging.Contracts;

namespace Common.Security.DataProtection.Tests;

public sealed class DefaultFieldProtectorTests
{
    private static DataProtectionPolicy DefaultPolicy() => new(new Dictionary<string, FieldProtection>
    {
        ["pan"] = new(ProtectionAction.Encrypt, "first6last4", RedactInLogs: true),
        ["amount"] = new(ProtectionAction.Clear, null, RedactInLogs: false),
        ["token"] = new(ProtectionAction.Encrypt, null, RedactInLogs: true),
    });

    private static (DefaultFieldProtector Protector, InMemoryKeyProvider Keys) Build(DataProtectionPolicy? policy = null)
    {
        var keys = new InMemoryKeyProvider();
        var protector = new DefaultFieldProtector(
            new AesGcmCryptoProvider(), keys, policy ?? DefaultPolicy(), new IMasker[] { new PanMasker() });
        return (protector, keys);
    }

    private static FieldProtectionContext Ctx(string field, long seq = 101) => new("file-abc", seq, field);

    [Fact]
    public void Protect_EncryptField_ProducesEncryptedValueStampedWithActiveKey()
    {
        var (protector, keys) = Build();

        var result = protector.Protect(Ctx("pan"), new ClearFieldValue("1234567890123456"));

        var encrypted = Assert.IsType<EncryptedFieldValue>(result);
        Assert.Equal("AES-256-GCM", encrypted.Value.Algorithm);
        Assert.Equal(keys.GetActiveKey().KeyId, encrypted.Value.KeyId);
    }

    [Theory]
    [InlineData("token")]
    public void RoundTrip_EncryptField_RecoversValue(string field)
    {
        var (protector, _) = Build();
        var original = new ClearFieldValue("secret-token-value");

        var protectedValue = protector.Protect(Ctx(field), original);
        var recovered = protector.Unprotect(Ctx(field), protectedValue);

        Assert.Equal(original, recovered);
    }

    [Fact]
    public void RoundTrip_PreservesTypedClearValues()
    {
        var (protector, _) = Build();

        foreach (var original in new FieldValue[]
                 {
                     new ClearFieldValue(221.73m),
                     new ClearFieldValue(true),
                     new ClearFieldValue(null),
                     new ClearFieldValue("plain"),
                 })
        {
            var roundTripped = protector.Unprotect(Ctx("token"), protector.Protect(Ctx("token"), original));
            Assert.Equal(original, roundTripped);
        }
    }

    [Fact]
    public void Protect_ClearField_ReturnsValueUnchanged()
    {
        var (protector, _) = Build();
        var value = new ClearFieldValue(221.73m);

        Assert.Same(value, protector.Protect(Ctx("amount"), value));
    }

    [Fact]
    public void Protect_AlreadyEncrypted_ReturnsValueUnchanged()
    {
        var (protector, _) = Build();
        var already = new EncryptedFieldValue(
            new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        Assert.Same(already, protector.Protect(Ctx("pan"), already));
    }

    [Fact]
    public void Unprotect_ClearValue_ReturnsValueUnchanged()
    {
        var (protector, _) = Build();
        var value = new ClearFieldValue(1m);

        Assert.Same(value, protector.Unprotect(Ctx("amount"), value));
    }

    [Fact]
    public void Unprotect_WithWrongContext_FailsAssociatedDataBinding()
    {
        var (protector, _) = Build();
        var protectedValue = protector.Protect(Ctx("pan", 101), new ClearFieldValue("1234567890123456"));

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Ctx("pan", 102), protectedValue));
    }

    [Fact]
    public void Unprotect_WithUnresolvableKey_Throws()
    {
        var (producer, _) = Build();
        var (consumer, _) = Build(); // different key provider instance
        var protectedValue = producer.Protect(Ctx("token"), new ClearFieldValue("x"));

        Assert.Throws<KeyNotFoundException>(() => consumer.Unprotect(Ctx("token"), protectedValue));
    }

    [Fact]
    public void Protect_UnclassifiedField_ThrowsFailClosed()
    {
        var (protector, _) = Build();

        Assert.Throws<KeyNotFoundException>(() => protector.Protect(Ctx("unknown"), new ClearFieldValue("x")));
    }

    [Fact]
    public void Mask_WithStrategy_MasksValue()
    {
        var (protector, _) = Build();

        Assert.Equal("123456******3456", protector.Mask(Ctx("pan"), new ClearFieldValue("1234567890123456")));
    }

    [Fact]
    public void Mask_WithoutStrategy_ReturnsClearString()
    {
        var (protector, _) = Build();

        Assert.Equal("221.73", protector.Mask(Ctx("amount"), new ClearFieldValue(221.73m)));
    }

    [Fact]
    public void Mask_EncryptedValue_Throws()
    {
        var (protector, _) = Build();
        var encrypted = new EncryptedFieldValue(
            new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        Assert.Throws<InvalidOperationException>(() => protector.Mask(Ctx("pan"), encrypted));
    }

    [Fact]
    public void Mask_UnknownStrategy_Throws()
    {
        var policy = new DataProtectionPolicy(new Dictionary<string, FieldProtection>
        {
            ["x"] = new(ProtectionAction.Clear, "ghost-strategy", RedactInLogs: false),
        });
        var (protector, _) = Build(policy);

        Assert.Throws<InvalidOperationException>(() => protector.Mask(Ctx("x"), new ClearFieldValue("value")));
    }

    [Fact]
    public void Constructor_WithNullArgument_Throws()
    {
        var crypto = new AesGcmCryptoProvider();
        var keys = new InMemoryKeyProvider();
        var policy = DefaultPolicy();
        var maskers = new IMasker[] { new PanMasker() };

        Assert.Throws<ArgumentNullException>(() => new DefaultFieldProtector(null!, keys, policy, maskers));
        Assert.Throws<ArgumentNullException>(() => new DefaultFieldProtector(crypto, null!, policy, maskers));
        Assert.Throws<ArgumentNullException>(() => new DefaultFieldProtector(crypto, keys, null!, maskers));
        Assert.Throws<ArgumentNullException>(() => new DefaultFieldProtector(crypto, keys, policy, null!));
    }
}
