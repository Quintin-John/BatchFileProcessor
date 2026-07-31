using Ingestion.Worker.Health;

namespace Ingestion.Worker.Tests;

public sealed class HealthTagsTests
{
    [Fact]
    public void Live_And_Ready_AreDistinctAndNonBlank()
    {
        // The liveness and readiness probes partition checks by these tags. If they were blank or equal,
        // a probe's predicate would match the wrong checks (or none — silently reporting Healthy).
        Assert.False(string.IsNullOrWhiteSpace(HealthTags.Live));
        Assert.False(string.IsNullOrWhiteSpace(HealthTags.Ready));
        Assert.NotEqual(HealthTags.Live, HealthTags.Ready);
    }
}
