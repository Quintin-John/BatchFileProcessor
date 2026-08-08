using Common.FileIngestion.Layouts;
using Common.Security.Encryption;
using Ingestion.Worker;

namespace Ingestion.Worker.Tests;

public sealed class LayoutProtectionPolicyTests
{
    // Named for the flag the layout carries, so nothing here implies what a field holds.
    private const string FlaggedField = "flagged";
    private const string UnflaggedField = "unflagged";

    private static Layout Layout() => new("1.0", 10, "ascii", 0, 1, 2, new[]
    {
        new RecordDefinition("r", "M", new[]
        {
            new FieldDefinition(UnflaggedField, 1, 4),
            new FieldDefinition(FlaggedField, 5, 6, encrypt: true),
        }),
    });

    [Fact]
    public void From_TheEncryptFlag_DecidesWhetherAFieldIsEncrypted()
    {
        var policy = LayoutProtectionPolicy.From(Layout());

        Assert.Equal(ProtectionAction.Encrypt, policy.Fields[FlaggedField]);
        Assert.Equal(ProtectionAction.Clear, policy.Fields[UnflaggedField]);
    }

    [Fact]
    public void From_CoversEveryLayoutField_SoLookupNeverThrows()
    {
        var policy = LayoutProtectionPolicy.From(Layout());

        Assert.Equal(2, policy.Fields.Count);
        Assert.Equal(ProtectionAction.Clear, policy.GetProtection(UnflaggedField));
        Assert.Equal(ProtectionAction.Encrypt, policy.GetProtection(FlaggedField));
    }

    [Fact]
    public void From_SameFieldName_FlaggedTheSameWayEverywhere_IsAllowed()
    {
        // The same name may legitimately recur across record types as long as the flag agrees (e.g. a
        // shared filler). Collapsing consistent duplicates is safe and must not throw.
        var layout = new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
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

        Assert.Equal(ProtectionAction.Clear, policy.GetProtection("shared"));
    }

    [Fact]
    public void From_SameFieldName_FlaggedInOnePlaceButNotAnother_Throws()
    {
        // 'dup' is flagged in one record type and not the other. Collapsing it would silently stop
        // encrypting one side, so construction must fail closed.
        var layout = new Layout("1.0", 10, "ascii", 0, 1, 2, new[]
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

    [Fact]
    public void From_DelimitedLayout_IsDecidedTheSameWay()
    {
        // Framing-agnostic: the policy reads the shared ILayout surface, so a delimited profile protects
        // its fields without any delimited-specific branch here.
        var layout = new DelimitedLayout("1.0", "\t", '\n', "ascii", new[]
        {
            new DelimitedRowDefinition("body", RowRole.Data, 0, new[]
            {
                new DelimitedFieldDefinition(UnflaggedField, 0),
                new DelimitedFieldDefinition(FlaggedField, 1, encrypt: true),
            }),
        });

        var policy = LayoutProtectionPolicy.From(layout);

        Assert.Equal(ProtectionAction.Encrypt, policy.Fields[FlaggedField]);
        Assert.Equal(ProtectionAction.Clear, policy.Fields[UnflaggedField]);
    }

    [Fact]
    public void From_DelimitedLayout_FlaggedInOneRowTypeButNotAnother_FailsClosed()
    {
        // Same fail-closed rule as fixed-width: one name must not carry two different flags, or the last
        // one written would silently stop encrypting the other.
        var layout = new DelimitedLayout("1.0", "\t", '\n', "ascii", new[]
        {
            new DelimitedRowDefinition("head", RowRole.Header, 1, new[]
            {
                new DelimitedFieldDefinition("dup", 0, encrypt: true),
            }),
            new DelimitedRowDefinition("body", RowRole.Data, 0, new[]
            {
                new DelimitedFieldDefinition("dup", 0),
            }),
        });

        var ex = Assert.Throws<InvalidOperationException>(() => LayoutProtectionPolicy.From(layout));
        Assert.Contains("dup", ex.Message, StringComparison.Ordinal);
    }
}
