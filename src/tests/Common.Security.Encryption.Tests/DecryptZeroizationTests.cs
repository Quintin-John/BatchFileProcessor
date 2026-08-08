using System.Text;
using System.Text.Json;
using Common.Messaging.Contracts;

namespace Common.Security.Encryption.Tests;

/// <summary>
/// Proves the decrypt paths zero the transient cleartext buffer (symmetric with the encrypt paths),
/// on both the success and the failure branch. A stub crypto returns a buffer the test still holds a
/// reference to, so the in-place zeroing is directly observable.
/// </summary>
public sealed class DecryptZeroizationTests
{
    // Arbitrary text: the crypto path does not interpret its input, so nothing about a payment record
    // makes this test any stronger.
    private const string RoundTrippedText = "some record text";

    private const string EncryptedField = "encrypted";

    private static EncryptionPolicy Policy() => new(new Dictionary<string, ProtectionAction>
    {
        [EncryptedField] = ProtectionAction.Encrypt,
    });

    private static (EncryptedFieldValue Envelope, InMemoryKeyProvider Keys) ResolvableEnvelope()
    {
        var keys = new InMemoryKeyProvider();
        var active = keys.GetActiveKey();
        var envelope = new EncryptedFieldValue(
            new EncryptedValue("STUB", active.KeyId, active.KeyVersion, "AA==", "AA==", "AA=="));
        return (envelope, keys);
    }

    private static FieldProtectionContext Ctx(string field) => new("file-abc", 101, field);

    [Fact]
    public void FieldProtector_Unprotect_RecoversValue_AndZeroesCleartext()
    {
        var (envelope, keys) = ResolvableEnvelope();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes((FieldValue)new ClearFieldValue("secret"), MessagingJson.Options);
        Assert.Contains(plaintext, b => b != 0); // precondition: the buffer really holds cleartext
        var protector = new DefaultFieldProtector(new BufferStubCrypto(plaintext), keys, Policy());

        var recovered = protector.Unprotect(Ctx(EncryptedField), envelope);

        Assert.Equal(new ClearFieldValue("secret"), recovered);
        Assert.DoesNotContain(plaintext, b => b != 0); // transient cleartext zeroed after use
    }

    [Fact]
    public void FieldProtector_Unprotect_ZeroesCleartext_EvenWhenDeserializeFails()
    {
        var (envelope, keys) = ResolvableEnvelope();
        var plaintext = Encoding.UTF8.GetBytes("{ not valid json"); // deserialization throws
        Assert.Contains(plaintext, b => b != 0);
        var protector = new DefaultFieldProtector(new BufferStubCrypto(plaintext), keys, Policy());

        Assert.ThrowsAny<JsonException>(() => protector.Unprotect(Ctx(EncryptedField), envelope));
        Assert.DoesNotContain(plaintext, b => b != 0); // zeroed on the failure path too (finally)
    }

    [Fact]
    public void PayloadProtector_Unprotect_RecoversPayload_AndZeroesCleartext()
    {
        var (envelope, keys) = ResolvableEnvelope();
        var plaintext = Encoding.UTF8.GetBytes(RoundTrippedText);
        Assert.Contains(plaintext, b => b != 0);
        var protector = new DefaultPayloadProtector(new BufferStubCrypto(plaintext), keys);

        var recovered = protector.Unprotect(Ctx("__raw_record__"), envelope);

        Assert.Equal(RoundTrippedText, recovered);
        Assert.DoesNotContain(plaintext, b => b != 0);
    }

    /// <summary>Crypto stub whose <see cref="Decrypt"/> returns the exact buffer the test supplied,
    /// so the caller's in-place zeroing is observable. Encrypt is unused by these tests.</summary>
    private sealed class BufferStubCrypto : ICryptoProvider
    {
        private readonly byte[] _plaintext;

        public BufferStubCrypto(byte[] plaintext) => _plaintext = plaintext;

        public string Algorithm => "STUB";

        public EncryptedValue Encrypt(ReadOnlySpan<byte> plaintext, DataKey key, ReadOnlySpan<byte> associatedData) =>
            new(Algorithm, key.KeyId, key.KeyVersion, "AA==", "AA==", "AA==");

        public byte[] Decrypt(EncryptedValue value, DataKey key, ReadOnlySpan<byte> associatedData) => _plaintext;
    }
}
