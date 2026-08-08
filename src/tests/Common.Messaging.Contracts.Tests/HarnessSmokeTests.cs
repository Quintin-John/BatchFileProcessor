namespace Common.Messaging.Contracts.Tests;

/// <summary>
/// Slice 0 — proves the test harness executes and is wired to the solution.
/// Real contract behaviour is covered from Slice 1 onward; this class is the
/// minimal signal that xUnit runs and the coverage gate is active.
/// </summary>
public sealed class HarnessSmokeTests
{
    [Fact]
    public void TestHarness_Executes()
    {
        Assert.True(true);
    }
}
