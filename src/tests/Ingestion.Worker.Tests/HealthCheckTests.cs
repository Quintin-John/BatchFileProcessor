using Common.FileIngestion.Health;
using Ingestion.Worker.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AspNetHealth = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

namespace Ingestion.Worker.Tests;

public sealed class HealthCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTime(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static async Task<AspNetHealth> CheckAsync(IHealthCheck check) =>
        (await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status;

    [Fact]
    public async Task Liveness_HealthyWhileBeating_UnhealthyOnceStale()
    {
        var time = new FakeTime(Start);
        var heartbeat = new Heartbeat(time);
        var check = new LivenessHealthCheck(new LivenessProbe(heartbeat, TimeSpan.FromSeconds(30)));

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(AspNetHealth.Healthy, await CheckAsync(check));

        time.Advance(TimeSpan.FromSeconds(40)); // past threshold
        Assert.Equal(AspNetHealth.Unhealthy, await CheckAsync(check));
    }

    [Fact]
    public async Task Readiness_ReflectsGate_HealthyOrDegraded()
    {
        var gate = new ReadinessGate();
        var check = new ReadinessHealthCheck(gate);

        Assert.Equal(AspNetHealth.Healthy, await CheckAsync(check));

        gate.MarkDegraded();
        Assert.Equal(AspNetHealth.Degraded, await CheckAsync(check));
    }

    [Fact]
    public void Constructors_NullArgument_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new LivenessHealthCheck(null!));
        Assert.Throws<ArgumentNullException>(() => new ReadinessHealthCheck(null!));
    }
}
