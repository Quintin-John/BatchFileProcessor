using Common.FileIngestion.Health;

namespace Common.FileIngestion.Tests.Health;

public sealed class HealthTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTime(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public void Heartbeat_TracksElapsedSinceLastBeat_AndResetsOnBeat()
    {
        var time = new FakeTime(Start);
        var heartbeat = new Heartbeat(time);

        Assert.Equal(TimeSpan.Zero, heartbeat.TimeSinceLastBeat);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), heartbeat.TimeSinceLastBeat);

        heartbeat.Beat();
        Assert.Equal(TimeSpan.Zero, heartbeat.TimeSinceLastBeat);
    }

    [Fact]
    public void Heartbeat_NullTimeProvider_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new Heartbeat(null!));

    [Fact]
    public void Liveness_HealthyWithinThreshold_UnhealthyOnceStale_HealthyAfterBeat()
    {
        var time = new FakeTime(Start);
        var heartbeat = new Heartbeat(time);
        var probe = new LivenessProbe(heartbeat, TimeSpan.FromSeconds(30));

        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(HealthStatus.Healthy, probe.Status);

        time.Advance(TimeSpan.FromSeconds(20)); // 40s total, past threshold
        Assert.Equal(HealthStatus.Unhealthy, probe.Status);

        heartbeat.Beat();
        Assert.Equal(HealthStatus.Healthy, probe.Status);
    }

    [Fact]
    public void Liveness_NullHeartbeat_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new LivenessProbe(null!, TimeSpan.FromSeconds(1)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Liveness_NonPositiveThreshold_Throws(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LivenessProbe(new Heartbeat(new FakeTime(Start)), TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Readiness_DefaultsHealthy_TogglesDegradedAndBack()
    {
        var gate = new ReadinessGate();
        Assert.Equal(HealthStatus.Healthy, gate.Status);

        gate.MarkDegraded();
        Assert.Equal(HealthStatus.Degraded, gate.Status);

        gate.MarkHealthy();
        Assert.Equal(HealthStatus.Healthy, gate.Status);
    }
}
