using Common.FileIngestion.Protection;
using Common.Messaging.Contracts;
using Common.Security.Encryption;

namespace Common.FileIngestion.Tests.Protection;

public sealed class RecordProtectorTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 1200;

    // Field names say only what the stub policy does with them. Which fields are sensitive is the layout's
    // business; all this class tests is that RecordProtector honours whatever it is told.
    private const string EncryptedField = "encrypted";
    private const string ClearField = "clear";

    private static RecordProtector Protector() => new(new StubProtector(), new StubPayloadProtector());

    // Arbitrary values; the protector does not interpret them.
    private const string SomeValue = "some value";
    private const decimal SomeNumber = 221.73m;

    private static IngestRecord Record(params (string Name, FieldValue Value)[] fields)
    {
        var dict = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        foreach (var (name, value) in fields)
        {
            dict[name] = value;
        }

        return new IngestRecord(new RecordLocator(101, 0, RecordExtent, "TRAN"), dict);
    }

    [Fact]
    public void Protect_EncryptsSensitiveFields_PassesOthers()
    {
        var result = Protector().Protect("file-abc", Record(
            (EncryptedField, new ClearFieldValue(SomeValue)),
            (ClearField, new ClearFieldValue(SomeNumber))));

        Assert.IsType<EncryptedFieldValue>(result.Fields[EncryptedField]);
        Assert.Equal(new ClearFieldValue(SomeNumber), result.Fields[ClearField]);
        Assert.Equal(101, result.Locator.RecordSeq);
    }

    [Fact]
    public void Protect_NoFieldEncrypted_ReturnsSameInstance_NoCopy()
    {
        // All fields pass through (nothing encrypts) -> copy-on-write returns the original record untouched.
        var record = Record((ClearField, new ClearFieldValue(SomeNumber)));

        var result = Protector().Protect("file-abc", record);

        Assert.Same(record, result);
    }

    [Fact]
    public void Protect_SomeFieldEncrypted_ReturnsNewRecord_PreservingOtherFields()
    {
        var record = Record(
            (EncryptedField, new ClearFieldValue(SomeValue)),
            (ClearField, new ClearFieldValue(SomeNumber)));

        var result = Protector().Protect("file-abc", record);

        Assert.NotSame(record, result); // a change materialises a new record
        Assert.IsType<EncryptedFieldValue>(result.Fields[EncryptedField]);
        Assert.Equal(new ClearFieldValue(SomeNumber), result.Fields[ClearField]); // untouched field carried over
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
        var result = Protector().ProtectRaw("file-abc", 7, "some unparsed record text");

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
            () => Protector().Protect(" ", Record((ClearField, new ClearFieldValue(1m)))));

    // Stands in for the real protector: encrypts the one field this fixture declares encrypted, passes the
    // one it declares clear, and throws on anything else so a field nobody classified cannot slip through.
    // It follows the fixture's declaration rather than naming fields itself — otherwise these tests would
    // prove the stub works rather than that RecordProtector honours what it is told.
    private sealed class StubProtector : IFieldProtector
    {
        public FieldValue Protect(FieldProtectionContext context, FieldValue value) => context.Field switch
        {
            EncryptedField => new EncryptedFieldValue(
                new EncryptedValue("AES-256-GCM", "k", "v", "bm9uY2U=", "Y2lwaGVy", "dGFn")),
            ClearField => value,
            _ => throw new KeyNotFoundException(
                $"Field '{context.Field}' is not in the policy, so nothing says whether it must be encrypted."),
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
