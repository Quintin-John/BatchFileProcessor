using System.Security.Cryptography;
using Common.Messaging.Contracts;

namespace Common.Security.Encryption.Tests;

public sealed class DefaultFieldProtectorTests
{
    private const string FieldText = "some field value";

    // Named for what the policy does with them, so nothing here implies what a field holds.
    private const string EncryptedField = "encrypted";
    private const string ClearField = "clear";
    private const string OtherEncryptedField = "encrypted-too";

    private static EncryptionPolicy DefaultPolicy() => new(new Dictionary<string, ProtectionAction>
    {
        [EncryptedField] = ProtectionAction.Encrypt,
        [ClearField] = ProtectionAction.Clear,
        [OtherEncryptedField] = ProtectionAction.Encrypt,
    });

    private static (DefaultFieldProtector Protector, InMemoryKeyProvider Keys) Build(EncryptionPolicy? policy = null)
    {
        var keys = new InMemoryKeyProvider();
        var protector = new DefaultFieldProtector(new AesGcmCryptoProvider(), keys, policy ?? DefaultPolicy());
        return (protector, keys);
    }

    private static FieldProtectionContext Ctx(string field, long seq = 101) => new("file-abc", seq, field);

    [Fact]
    public void Protect_EncryptField_ProducesEncryptedValueStampedWithActiveKey()
    {
        var (protector, keys) = Build();

        var result = protector.Protect(Ctx(EncryptedField), new ClearFieldValue(FieldText));

        var encrypted = Assert.IsType<EncryptedFieldValue>(result);
        Assert.Equal("AES-256-GCM", encrypted.Value.Algorithm);
        Assert.Equal(keys.GetActiveKey().KeyId, encrypted.Value.KeyId);
    }

    [Theory]
    [InlineData(OtherEncryptedField)]
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
            var roundTripped = protector.Unprotect(Ctx(OtherEncryptedField), protector.Protect(Ctx(OtherEncryptedField), original));
            Assert.Equal(original, roundTripped);
        }
    }

    [Fact]
    public void Protect_ClearField_ReturnsValueUnchanged()
    {
        var (protector, _) = Build();
        var value = new ClearFieldValue(221.73m);

        Assert.Same(value, protector.Protect(Ctx(ClearField), value));
    }

    [Fact]
    public void Protect_AlreadyEncrypted_ReturnsValueUnchanged()
    {
        var (protector, _) = Build();
        var already = new EncryptedFieldValue(
            new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        Assert.Same(already, protector.Protect(Ctx(EncryptedField), already));
    }

    [Fact]
    public void Unprotect_ClearValue_ReturnsValueUnchanged()
    {
        var (protector, _) = Build();
        var value = new ClearFieldValue(1m);

        Assert.Same(value, protector.Unprotect(Ctx(ClearField), value));
    }

    [Fact]
    public void Unprotect_WithWrongContext_FailsAssociatedDataBinding()
    {
        var (protector, _) = Build();
        var protectedValue = protector.Protect(Ctx(EncryptedField, 101), new ClearFieldValue(FieldText));

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Ctx(EncryptedField, 102), protectedValue));
    }

    [Fact]
    public void Unprotect_WithUnresolvableKey_Throws()
    {
        var (producer, _) = Build();
        var (consumer, _) = Build(); // different key provider instance
        var protectedValue = producer.Protect(Ctx(OtherEncryptedField), new ClearFieldValue("x"));

        Assert.Throws<KeyNotFoundException>(() => consumer.Unprotect(Ctx(OtherEncryptedField), protectedValue));
    }

    [Fact]
    public void Protect_UnclassifiedField_ThrowsFailClosed()
    {
        var (protector, _) = Build();

        Assert.Throws<KeyNotFoundException>(() => protector.Protect(Ctx("unknown"), new ClearFieldValue("x")));
    }

    [Fact]
    public void Constructor_WithNullArgument_Throws()
    {
        var crypto = new AesGcmCryptoProvider();
        var keys = new InMemoryKeyProvider();
        var policy = DefaultPolicy();

        Assert.Throws<ArgumentNullException>(() => new DefaultFieldProtector(null!, keys, policy));
        Assert.Throws<ArgumentNullException>(() => new DefaultFieldProtector(crypto, null!, policy));
        Assert.Throws<ArgumentNullException>(() => new DefaultFieldProtector(crypto, keys, null!));
    }
}
