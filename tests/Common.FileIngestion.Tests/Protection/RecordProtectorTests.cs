using Common.FileIngestion.Protection;
using Common.Messaging.Contracts;
using Common.Security.DataProtection;

namespace Common.FileIngestion.Tests.Protection;

public sealed class RecordProtectorTests
{
    private static RecordProtector Protector() => new(new StubProtector(), new StubPayloadProtector());

    private static IngestRecord Record(params (string Name, FieldValue Value)[] fields)
    {
        var dict = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        foreach (var (name, value) in fields)
        {
            dict[name] = value;
        }

        return new IngestRecord(new RecordLocator(101, 0, "TRAN"), dict);
    }

    [Fact]
    public void Protect_EncryptsSensitiveFields_PassesOthers()
    {
        var result = Protector().Protect("file-abc", Record(
            ("pan", new ClearFieldValue("1234567890123456")),
            ("amount", new ClearFieldValue(221.73m))));

        Assert.IsType<EncryptedFieldValue>(result.Fields["pan"]);
        Assert.Equal(new ClearFieldValue(221.73m), result.Fields["amount"]);
        Assert.Equal(101, result.Locator.RecordSeq);
    }

    [Fact]
    public void Protect_NoFieldEncrypted_ReturnsSameInstance_NoCopy()
    {
        // All fields pass through (nothing encrypts) -> copy-on-write returns the original record untouched.
        var record = Record(("amount", new ClearFieldValue(221.73m)));

        var result = Protector().Protect("file-abc", record);

        Assert.Same(record, result);
    }

    [Fact]
    public void Protect_SomeFieldEncrypted_ReturnsNewRecord_PreservingOtherFields()
    {
        var record = Record(
            ("pan", new ClearFieldValue("1234567890123456")),
            ("amount", new ClearFieldValue(221.73m)));

        var result = Protector().Protect("file-abc", record);

        Assert.NotSame(record, result); // a change materialises a new record
        Assert.IsType<EncryptedFieldValue>(result.Fields["pan"]);
        Assert.Equal(new ClearFieldValue(221.73m), result.Fields["amount"]); // untouched field carried over
        Assert.Equal(2, result.Fields.Count);
    }

    [Fact]
    public void Protect_UnclassifiedField_PropagatesFailClosed()
    {
        Assert.Throws<KeyNotFoundException>(
            () => Protector().Protect("file-abc", Record(("unclassified", new ClearFieldValue("x")))));
    }

    [Fact]
    public void ProtectRaw_EncryptsRawRecord()
    {
        var result = Protector().ProtectRaw("file-abc", 7, "HEAD...4111111111111111");

        Assert.IsType<EncryptedFieldValue>(result);
    }

    [Fact]
    public void ProtectRaw_BlankFileId_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => Protector().ProtectRaw(" ", 1, "raw"));

    [Fact]
    public void ProtectRaw_NullRaw_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Protector().ProtectRaw("f", 1, null!));

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordProtector(null!, new StubPayloadProtector()));
        Assert.Throws<ArgumentNullException>(() => new RecordProtector(new StubProtector(), null!));
    }

    [Fact]
    public void Protect_NullRecord_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Protector().Protect("f", null!));

    [Fact]
    public void Protect_BlankFileId_Throws() =>
        Assert.ThrowsAny<ArgumentException>(
            () => Protector().Protect(" ", Record(("amount", new ClearFieldValue(1m)))));

    // Encrypts "pan", passes "amount", rejects anything else (fail-closed classification).
    private sealed class StubProtector : IFieldProtector
    {
        public FieldValue Protect(FieldProtectionContext context, FieldValue value) => context.Field switch
        {
            "pan" => new EncryptedFieldValue(new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn")),
            "amount" => value,
            _ => throw new KeyNotFoundException($"Field '{context.Field}' is not classified."),
        };

        public FieldValue Unprotect(FieldProtectionContext context, FieldValue value) => value;
    }

    private sealed class StubPayloadProtector : IPayloadProtector
    {
        public EncryptedFieldValue Protect(FieldProtectionContext context, string payload) =>
            new(new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn"));

        public string Unprotect(FieldProtectionContext context, EncryptedFieldValue payload) => string.Empty;
    }
}
