namespace Common.Messaging.Contracts.Tests;

public sealed class EncryptedValueTests
{
    private static EncryptedValue CreateValid() =>
        new("AES-256-GCM", "key-id", "key-version", "bm9uY2U=", "Y2lwaGVy", "dGFn");

    [Fact]
    public void Constructor_WithValidArguments_SetsAllProperties()
    {
        var value = new EncryptedValue(
            algorithm: "AES-256-GCM",
            keyId: "key-id",
            keyVersion: "v2",
            nonce: "bm9uY2U=",
            ciphertext: "Y2lwaGVy",
            tag: "dGFn");

        Assert.Equal("AES-256-GCM", value.Algorithm);
        Assert.Equal("key-id", value.KeyId);
        Assert.Equal("v2", value.KeyVersion);
        Assert.Equal("bm9uY2U=", value.Nonce);
        Assert.Equal("Y2lwaGVy", value.Ciphertext);
        Assert.Equal("dGFn", value.Tag);
    }

    [Theory]
    // algorithm invalid
    [InlineData(null, "k", "v", "n", "c", "t")]
    [InlineData("", "k", "v", "n", "c", "t")]
    [InlineData("  ", "k", "v", "n", "c", "t")]
    // keyId invalid
    [InlineData("a", null, "v", "n", "c", "t")]
    [InlineData("a", "", "v", "n", "c", "t")]
    // keyVersion invalid
    [InlineData("a", "k", null, "n", "c", "t")]
    [InlineData("a", "k", "  ", "n", "c", "t")]
    // nonce invalid
    [InlineData("a", "k", "v", null, "c", "t")]
    [InlineData("a", "k", "v", "", "c", "t")]
    // ciphertext invalid
    [InlineData("a", "k", "v", "n", null, "t")]
    [InlineData("a", "k", "v", "n", "  ", "t")]
    // tag invalid
    [InlineData("a", "k", "v", "n", "c", null)]
    [InlineData("a", "k", "v", "n", "c", "")]
    public void Constructor_WithNullEmptyOrWhitespaceArgument_Throws(
        string? algorithm, string? keyId, string? keyVersion, string? nonce, string? ciphertext, string? tag)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new EncryptedValue(algorithm!, keyId!, keyVersion!, nonce!, ciphertext!, tag!));
    }

    [Fact]
    public void Equality_WithIdenticalValues_AreEqual()
    {
        var a = CreateValid();
        var b = CreateValid();

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [InlineData("AES-256-GCM-SIV", "key-id", "key-version", "bm9uY2U=", "Y2lwaGVy", "dGFn")]
    [InlineData("AES-256-GCM", "other-key", "key-version", "bm9uY2U=", "Y2lwaGVy", "dGFn")]
    [InlineData("AES-256-GCM", "key-id", "v3", "bm9uY2U=", "Y2lwaGVy", "dGFn")]
    [InlineData("AES-256-GCM", "key-id", "key-version", "b3RoZXI=", "Y2lwaGVy", "dGFn")]
    [InlineData("AES-256-GCM", "key-id", "key-version", "bm9uY2U=", "b3RoZXI=", "dGFn")]
    [InlineData("AES-256-GCM", "key-id", "key-version", "bm9uY2U=", "Y2lwaGVy", "b3RoZXI=")]
    public void Equality_WhenAnyMemberDiffers_AreNotEqual(
        string algorithm, string keyId, string keyVersion, string nonce, string ciphertext, string tag)
    {
        var baseline = CreateValid();
        var other = new EncryptedValue(algorithm, keyId, keyVersion, nonce, ciphertext, tag);

        Assert.NotEqual(baseline, other);
    }
}
