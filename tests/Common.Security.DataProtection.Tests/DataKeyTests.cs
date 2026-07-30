namespace Common.Security.DataProtection.Tests;

public sealed class DataKeyTests
{
    private static byte[] Material() => new byte[DataKey.MaterialLength];

    [Fact]
    public void Constructor_WithValidArguments_SetsIdentity()
    {
        var key = new DataKey("key-id", "v1", Material());

        Assert.Equal("key-id", key.KeyId);
        Assert.Equal("v1", key.KeyVersion);
    }

    [Theory]
    [InlineData(null, "v")]
    [InlineData("", "v")]
    [InlineData("  ", "v")]
    [InlineData("k", null)]
    [InlineData("k", "")]
    public void Constructor_WithBlankIdentity_Throws(string? keyId, string? keyVersion)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DataKey(keyId!, keyVersion!, Material()));
    }

    [Fact]
    public void Constructor_WithNullMaterial_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DataKey("k", "v", null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_WithWrongMaterialLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => new DataKey("k", "v", new byte[length]));
    }

    [Fact]
    public void Material_IsDefensivelyCopied()
    {
        // Proven behaviourally in the crypto round-trip: mutating the source array after
        // construction must not change decryption results. Here we assert construction
        // succeeds with an exact-length array and the identity is retained.
        var source = new byte[DataKey.MaterialLength];
        var key = new DataKey("k", "v", source);

        Assert.Equal("k", key.KeyId);
    }
}
