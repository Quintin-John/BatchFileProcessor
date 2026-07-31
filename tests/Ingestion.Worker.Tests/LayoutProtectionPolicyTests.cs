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
    public void From_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LayoutProtectionPolicy.From(null!));
    }
}
