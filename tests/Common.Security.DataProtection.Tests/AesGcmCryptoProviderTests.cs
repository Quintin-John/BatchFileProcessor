using System.Security.Cryptography;
using System.Text;
using Common.Messaging.Contracts;

namespace Common.Security.DataProtection.Tests;

public sealed class AesGcmCryptoProviderTests
{
    private static readonly AesGcmCryptoProvider Provider = new();

    private static DataKey Key(byte fill = 7) =>
        new("key-id", "v1", Enumerable.Repeat(fill, DataKey.MaterialLength).ToArray());

    private static byte[] Aad(string s = "file-abc:101:amount") => Encoding.UTF8.GetBytes(s);

    private static byte[] Plain(string s = "221.73") => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void RoundTrip_RecoversPlaintext()
    {
        var key = Key();
        var aad = Aad();

        var envelope = Provider.Encrypt(Plain(), key, aad);
        var recovered = Provider.Decrypt(envelope, key, aad);

        Assert.Equal("221.73", Encoding.UTF8.GetString(recovered));
    }

    [Fact]
    public void RoundTrip_WithEmptyAssociatedData_Works()
    {
        var key = Key();

        var envelope = Provider.Encrypt(Plain("x"), key, ReadOnlySpan<byte>.Empty);
        var recovered = Provider.Decrypt(envelope, key, ReadOnlySpan<byte>.Empty);

        Assert.Equal("x", Encoding.UTF8.GetString(recovered));
    }

    [Fact]
    public void Encrypt_PopulatesSelfDescribingEnvelope()
    {
        var envelope = Provider.Encrypt(Plain(), Key(), Aad());

        Assert.Equal("AES-256-GCM", envelope.Algorithm);
        Assert.Equal("key-id", envelope.KeyId);
        Assert.Equal("v1", envelope.KeyVersion);
        Assert.False(string.IsNullOrEmpty(envelope.Nonce));
        Assert.False(string.IsNullOrEmpty(envelope.Ciphertext));
        Assert.False(string.IsNullOrEmpty(envelope.Tag));
    }

    [Fact]
    public void Encrypt_UsesAFreshNonceEachCall()
    {
        var key = Key();

        var a = Provider.Encrypt(Plain(), key, Aad());
        var b = Provider.Encrypt(Plain(), key, Aad());

        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
    }

    [Fact]
    public void Encrypt_WithEmptyPlaintext_Throws()
    {
        Assert.Throws<ArgumentException>(() => Provider.Encrypt(ReadOnlySpan<byte>.Empty, Key(), Aad()));
    }

    [Fact]
    public void Encrypt_WithNullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Provider.Encrypt(Plain(), null!, Aad()));
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_Throws()
    {
        var key = Key();
        var envelope = Provider.Encrypt(Plain(), key, Aad());
        var tampered = WithFlippedCiphertext(envelope);

        Assert.ThrowsAny<CryptographicException>(() => Provider.Decrypt(tampered, key, Aad()));
    }

    [Fact]
    public void Decrypt_WithTamperedTag_Throws()
    {
        var key = Key();
        var envelope = Provider.Encrypt(Plain(), key, Aad());
        var tag = Convert.FromBase64String(envelope.Tag);
        tag[0] ^= 0xFF;
        var tampered = new EncryptedValue(envelope.Algorithm, envelope.KeyId, envelope.KeyVersion,
            envelope.Nonce, envelope.Ciphertext, Convert.ToBase64String(tag));

        Assert.ThrowsAny<CryptographicException>(() => Provider.Decrypt(tampered, key, Aad()));
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var envelope = Provider.Encrypt(Plain(), Key(7), Aad());

        Assert.ThrowsAny<CryptographicException>(() => Provider.Decrypt(envelope, Key(9), Aad()));
    }

    [Fact]
    public void Decrypt_WithWrongAssociatedData_Throws()
    {
        var key = Key();
        var envelope = Provider.Encrypt(Plain(), key, Aad("file-abc:101:amount"));

        Assert.ThrowsAny<CryptographicException>(() => Provider.Decrypt(envelope, key, Aad("file-abc:101:pan")));
    }

    [Fact]
    public void Decrypt_WithAlgorithmMismatch_Throws()
    {
        var key = Key();
        var envelope = Provider.Encrypt(Plain(), key, Aad());
        var foreign = new EncryptedValue("SOME-OTHER-ALG", envelope.KeyId, envelope.KeyVersion,
            envelope.Nonce, envelope.Ciphertext, envelope.Tag);

        Assert.Throws<InvalidOperationException>(() => Provider.Decrypt(foreign, key, Aad()));
    }

    [Fact]
    public void Decrypt_WithNullEnvelope_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Provider.Decrypt(null!, Key(), Aad()));
    }

    private static EncryptedValue WithFlippedCiphertext(EncryptedValue envelope)
    {
        var ct = Convert.FromBase64String(envelope.Ciphertext);
        ct[0] ^= 0xFF;
        return new EncryptedValue(envelope.Algorithm, envelope.KeyId, envelope.KeyVersion,
            envelope.Nonce, Convert.ToBase64String(ct), envelope.Tag);
    }
}
