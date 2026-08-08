using System.Security.Cryptography;
using Common.Messaging.Contracts;

namespace Common.Security.DataProtection.Tests;

public sealed class DefaultPayloadProtectorTests
{
    // Stands for a record that failed to parse, so nothing said which of its values were sensitive.
    private const string UnparsedRecordText = "some unparsed record text";

    private static (DefaultPayloadProtector Protector, InMemoryKeyProvider Keys) Build()
    {
        var keys = new InMemoryKeyProvider();
        return (new DefaultPayloadProtector(new AesGcmCryptoProvider(), keys), keys);
    }

    private static FieldProtectionContext Ctx(long seq = 101) => new("file-abc", seq, "__raw_record__");

    [Fact]
    public void Protect_ProducesEncryptedValue_StampedWithActiveKey()
    {
        var (protector, keys) = Build();

        var result = protector.Protect(Ctx(), UnparsedRecordText);

        Assert.Equal("AES-256-GCM", result.Value.Algorithm);
        Assert.Equal(keys.GetActiveKey().KeyId, result.Value.KeyId);
    }

    [Fact]
    public void RoundTrip_RecoversPayload()
    {
        var (protector, _) = Build();
        const string payload = UnparsedRecordText;

        var recovered = protector.Unprotect(Ctx(), protector.Protect(Ctx(), payload));

        Assert.Equal(payload, recovered);
    }

    [Fact]
    public void Protect_EmptyPayload_Throws()
    {
        var (protector, _) = Build();

        Assert.Throws<ArgumentException>(() => protector.Protect(Ctx(), string.Empty));
    }

    [Fact]
    public void Unprotect_WithWrongContext_FailsAssociatedDataBinding()
    {
        var (protector, _) = Build();
        var encrypted = protector.Protect(Ctx(101), "sensitive");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Ctx(102), encrypted));
    }

    [Fact]
    public void Unprotect_WithUnresolvableKey_Throws()
    {
        var (producer, _) = Build();
        var (consumer, _) = Build(); // different key provider
        var encrypted = producer.Protect(Ctx(), "x");

        Assert.Throws<KeyNotFoundException>(() => consumer.Unprotect(Ctx(), encrypted));
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultPayloadProtector(null!, new InMemoryKeyProvider()));
        Assert.Throws<ArgumentNullException>(() => new DefaultPayloadProtector(new AesGcmCryptoProvider(), null!));
    }

    [Fact]
    public void Protect_NullArgument_Throws()
    {
        var (protector, _) = Build();

        Assert.Throws<ArgumentNullException>(() => protector.Protect(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => protector.Protect(Ctx(), null!));
    }
}
