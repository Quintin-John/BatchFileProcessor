namespace Common.Messaging.Contracts.Tests;

public sealed class FieldValueTests
{
    private static EncryptedValue Envelope() =>
        new("AES-256-GCM", "key-id", "v1", "bm9uY2U=", "Y2lwaGVy", "dGFn");

    [Fact]
    public void ClearFieldValue_HoldsScalarValue()
    {
        var value = new ClearFieldValue(221.73m);

        Assert.Equal(221.73m, value.Value);
        Assert.IsType<FieldValue>(value, exactMatch: false);
    }

    [Fact]
    public void ClearFieldValue_AllowsNull_ForPresentButEmptyField()
    {
        var value = new ClearFieldValue(null);

        Assert.Null(value.Value);
    }

    [Fact]
    public void ClearFieldValue_WireSupportedTypes_ConstructAndCarryTheValue()
    {
        object[] supported =
        [
            "text",
            true,
            123.45m,
            9_000_000_000L,
            42,
            new DateOnly(2022, 11, 7),
            new DateTimeOffset(2022, 11, 7, 0, 0, 0, TimeSpan.Zero),
        ];

        foreach (var value in supported)
        {
            Assert.Equal(value, new ClearFieldValue(value).Value);
        }
    }

    [Fact]
    public void ClearFieldValue_UnsupportedTypes_ThrowAtConstruction()
    {
        object[] unsupported =
        [
            1.5d,                      // double: can overflow decimal on read — unsupported by design
            Guid.NewGuid(),
            new DateTime(2022, 11, 7), // DateTime (vs DateTimeOffset) is not a wire type
        ];

        foreach (var value in unsupported)
        {
            var ex = Assert.Throws<ArgumentException>(() => new ClearFieldValue(value));
            Assert.Equal("value", ex.ParamName);
        }
    }

    [Fact]
    public void EncryptedFieldValue_HoldsEnvelope()
    {
        var envelope = Envelope();
        var value = new EncryptedFieldValue(envelope);

        Assert.Same(envelope, value.Value);
        Assert.IsType<FieldValue>(value, exactMatch: false);
    }

    [Fact]
    public void EncryptedFieldValue_WithNullEnvelope_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EncryptedFieldValue(null!));
    }

    [Fact]
    public void Cases_AreDistinctTypes()
    {
        FieldValue clear = new ClearFieldValue("x");
        FieldValue encrypted = new EncryptedFieldValue(Envelope());

        Assert.IsType<ClearFieldValue>(clear);
        Assert.IsType<EncryptedFieldValue>(encrypted);
        Assert.NotEqual<FieldValue>(clear, encrypted);
    }

    [Fact]
    public void ClearFieldValue_Equality_ByValue()
    {
        Assert.Equal(new ClearFieldValue("abc"), new ClearFieldValue("abc"));
        Assert.NotEqual(new ClearFieldValue("abc"), new ClearFieldValue("xyz"));
    }

    [Fact]
    public void EncryptedFieldValue_Equality_ByEnvelope()
    {
        Assert.Equal(new EncryptedFieldValue(Envelope()), new EncryptedFieldValue(Envelope()));
    }
}
