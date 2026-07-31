using Common.FileIngestion.Layouts;
using Common.Security.DataProtection;
using Ingestion.Worker;

namespace Ingestion.Worker.Tests;

public sealed class LayoutProtectionPolicyTests
{
    private static Layout Layout() => new("1.0", 10, "ascii", 1, 2, new[]
    {
        new RecordDefinition("r", "M", new[]
        {
            new FieldDefinition("clearField", 1, 4),
            new FieldDefinition("pan", 5, 6, encrypt: true),
        }),
    });

    [Fact]
    public void From_FlaggedField_EncryptsAndRedacts_OthersClear()
    {
        var policy = LayoutProtectionPolicy.From(Layout());

        var pan = policy.Fields["pan"];
        Assert.Equal(ProtectionAction.Encrypt, pan.Action);
        Assert.True(pan.RedactInLogs);

        var clear = policy.Fields["clearField"];
        Assert.Equal(ProtectionAction.Clear, clear.Action);
        Assert.False(clear.RedactInLogs);
    }

    [Fact]
    public void From_ClassifiesEveryLayoutField_SoLookupNeverThrows()
    {
        var policy = LayoutProtectionPolicy.From(Layout());

        Assert.Equal(2, policy.Fields.Count);
        Assert.Equal(ProtectionAction.Clear, policy.GetProtection("clearField").Action);
        Assert.Equal(ProtectionAction.Encrypt, policy.GetProtection("pan").Action);
    }

    [Fact]
    public void From_SameFieldName_ConsistentClassification_AcrossRecordTypes_IsAllowed()
    {
        // The same name may legitimately recur across record types as long as it classifies identically
        // (e.g. a shared FILLER). Collapsing consistent duplicates is safe and must not throw.
        var layout = new Layout("1.0", 10, "ascii", 1, 2, new[]
        {
            new RecordDefinition("a", "AA", new[]
            {
                new FieldDefinition("shared", 1, 5),
                new FieldDefinition("tailA", 6, 5),
            }),
            new RecordDefinition("b", "BB", new[]
            {
                new FieldDefinition("shared", 1, 5),
                new FieldDefinition("tailB", 6, 5),
            }),
        });

        var policy = LayoutProtectionPolicy.From(layout);

        Assert.Equal(ProtectionAction.Clear, policy.GetProtection("shared").Action);
    }

    [Fact]
    public void From_SameFieldName_ConflictingClassification_AcrossRecordTypes_Throws()
    {
        // 'dup' is encrypted in one record type and clear in another. Collapsing it would silently
        // declassify the encrypted side, so construction must fail closed.
        var layout = new Layout("1.0", 10, "ascii", 1, 2, new[]
        {
            new RecordDefinition("a", "AA", new[]
            {
                new FieldDefinition("dup", 1, 5, encrypt: true),
                new FieldDefinition("tailA", 6, 5),
            }),
            new RecordDefinition("b", "BB", new[]
            {
                new FieldDefinition("dup", 1, 5),
                new FieldDefinition("tailB", 6, 5),
            }),
        });

        var ex = Assert.Throws<InvalidOperationException>(() => LayoutProtectionPolicy.From(layout));
        Assert.Contains("dup", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void From_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LayoutProtectionPolicy.From(null!));
    }
}
