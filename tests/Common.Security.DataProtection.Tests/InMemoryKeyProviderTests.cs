using System.Text;

namespace Common.Security.DataProtection.Tests;

public sealed class InMemoryKeyProviderTests
{
    [Fact]
    public void GetActiveKey_ReturnsAKeyWithIdentity()
    {
        var provider = new InMemoryKeyProvider();

        var key = provider.GetActiveKey();

        Assert.False(string.IsNullOrWhiteSpace(key.KeyId));
        Assert.False(string.IsNullOrWhiteSpace(key.KeyVersion));
    }

    [Fact]
    public void GetActiveKey_IsStableAcrossCalls()
    {
        var provider = new InMemoryKeyProvider();

        Assert.Same(provider.GetActiveKey(), provider.GetActiveKey());
    }

    [Fact]
    public void ResolveKey_WithActiveIdentity_ReturnsSameKey()
    {
        var provider = new InMemoryKeyProvider();
        var active = provider.GetActiveKey();

        var resolved = provider.ResolveKey(active.KeyId, active.KeyVersion);

        Assert.Same(active, resolved);
    }

    [Fact]
    public void ResolveKey_WithUnknownIdentity_Throws()
    {
        var provider = new InMemoryKeyProvider();

        Assert.Throws<KeyNotFoundException>(() => provider.ResolveKey("nope", "1"));
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData("", "1")]
    [InlineData("k", "  ")]
    public void ResolveKey_WithBlankIdentity_Throws(string? keyId, string? keyVersion)
    {
        var provider = new InMemoryKeyProvider();

        Assert.ThrowsAny<ArgumentException>(() => provider.ResolveKey(keyId!, keyVersion!));
    }

    [Fact]
    public void ActiveKey_ResolvedById_DecryptsWhatItEncrypted()
    {
        // Option A end-to-end: encrypt with the active key, resolve it later by the id stamped
        // into the envelope, and decrypt successfully.
        var provider = new InMemoryKeyProvider();
        var crypto = new AesGcmCryptoProvider();
        var aad = Encoding.UTF8.GetBytes("ctx");
        var active = provider.GetActiveKey();

        var envelope = crypto.Encrypt(Encoding.UTF8.GetBytes("secret"), active, aad);
        var resolved = provider.ResolveKey(envelope.KeyId, envelope.KeyVersion);
        var recovered = crypto.Decrypt(envelope, resolved, aad);

        Assert.Equal("secret", Encoding.UTF8.GetString(recovered));
    }
}
