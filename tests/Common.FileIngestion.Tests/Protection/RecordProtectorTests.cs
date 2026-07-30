using Common.FileIngestion.Protection;
using Common.Messaging.Contracts;
using Common.Security.DataProtection;

namespace Common.FileIngestion.Tests.Protection;

public sealed class RecordProtectorTests
{
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
        var protector = new RecordProtector(new StubProtector());

        var result = protector.Protect("file-abc", Record(
            ("pan", new ClearFieldValue("1234567890123456")),
            ("amount", new ClearFieldValue(221.73m))));

        Assert.IsType<EncryptedFieldValue>(result.Fields["pan"]);
        Assert.Equal(new ClearFieldValue(221.73m), result.Fields["amount"]);
        Assert.Equal(101, result.Locator.RecordSeq);
    }

    [Fact]
    public void Protect_UnclassifiedField_PropagatesFailClosed()
    {
        var protector = new RecordProtector(new StubProtector());

        Assert.Throws<KeyNotFoundException>(
            () => protector.Protect("file-abc", Record(("unclassified", new ClearFieldValue("x")))));
    }

    [Fact]
    public void Constructor_NullProtector_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RecordProtector(null!));

    [Fact]
    public void Protect_NullRecord_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RecordProtector(new StubProtector()).Protect("f", null!));

    [Fact]
    public void Protect_BlankFileId_Throws() =>
        Assert.ThrowsAny<ArgumentException>(
            () => new RecordProtector(new StubProtector()).Protect(" ", Record(("amount", new ClearFieldValue(1m)))));

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

        public string Mask(FieldProtectionContext context, FieldValue value) => string.Empty;
    }
}
