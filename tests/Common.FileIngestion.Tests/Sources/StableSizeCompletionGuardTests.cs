using Common.FileIngestion.Sources;

namespace Common.FileIngestion.Tests.Sources;

public sealed class StableSizeCompletionGuardTests
{
    private const string Path = "/incoming/file.dat";
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);

    private static (StableSizeCompletionGuard Guard, FakeProbe Probe, FakeClock Clock) Build()
    {
        var probe = new FakeProbe { LengthResult = 100, LastWriteResult = T0, CanOpenResult = true };
        var clock = new FakeClock(T0);
        return (new StableSizeCompletionGuard(Quiet, clock, probe), probe, clock);
    }

    [Fact]
    public void Complete_WhenSizeStableForQuietPeriod_AndOpenable()
    {
        var (guard, _, clock) = Build();

        Assert.False(guard.IsComplete(Path)); // first sighting starts the quiet period

        clock.Now = T0 + Quiet + TimeSpan.FromSeconds(1); // unchanged and quiet long enough

        Assert.True(guard.IsComplete(Path));
    }

    [Fact]
    public void Incomplete_BeforeQuietPeriodElapses()
    {
        var (guard, _, clock) = Build();

        Assert.False(guard.IsComplete(Path));
        clock.Now = T0 + TimeSpan.FromSeconds(3); // < quiet period

        Assert.False(guard.IsComplete(Path));
    }

    [Fact]
    public void Incomplete_WhileSizeStillGrowing_ThenComplete_AfterItSettles()
    {
        var (guard, probe, clock) = Build();

        Assert.False(guard.IsComplete(Path));            // seen at len 100
        clock.Now = T0 + TimeSpan.FromSeconds(6);
        probe.LengthResult = 200;                        // grew: resets the quiet period
        Assert.False(guard.IsComplete(Path));

        clock.Now = T0 + TimeSpan.FromSeconds(12);        // 6s of stability at len 200
        Assert.True(guard.IsComplete(Path));
    }

    [Fact]
    public void Incomplete_WhenLastWriteChanges_EvenIfSizeSame()
    {
        var (guard, probe, clock) = Build();

        Assert.False(guard.IsComplete(Path));
        clock.Now = T0 + TimeSpan.FromSeconds(6);
        probe.LastWriteResult = T0 + TimeSpan.FromSeconds(1); // rewritten in place: resets

        Assert.False(guard.IsComplete(Path));
    }

    [Fact]
    public void Incomplete_WhenWriterStillHoldsFileOpen()
    {
        var (guard, probe, clock) = Build();

        Assert.False(guard.IsComplete(Path));
        clock.Now = T0 + Quiet + TimeSpan.FromSeconds(1);
        probe.CanOpenResult = false; // stable + quiet, but a writer still holds it

        Assert.False(guard.IsComplete(Path));
    }

    [Fact]
    public void Incomplete_WhenFileMissing()
    {
        var (guard, probe, _) = Build();
        probe.ExistsResult = false;

        Assert.False(guard.IsComplete(Path));
    }

    [Fact]
    public void Constructor_NonPositiveQuietPeriod_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StableSizeCompletionGuard(TimeSpan.Zero, new FakeClock(T0), new FakeProbe()));

    [Fact]
    public void Constructor_NullTimeProvider_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new StableSizeCompletionGuard(Quiet, null!, new FakeProbe()));

    [Fact]
    public void Constructor_NullProbe_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new StableSizeCompletionGuard(Quiet, new FakeClock(T0), null!));

    [Fact]
    public void IsComplete_BlankPath_Throws()
    {
        var (guard, _, _) = Build();

        Assert.Throws<ArgumentException>(() => guard.IsComplete("  "));
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeProbe : IFileProbe
    {
        public bool ExistsResult { get; set; } = true;

        public long LengthResult { get; set; }

        public DateTimeOffset LastWriteResult { get; set; }

        public bool CanOpenResult { get; set; } = true;

        public bool Exists(string path) => ExistsResult;

        public long Length(string path) => LengthResult;

        public DateTimeOffset LastWriteTimeUtc(string path) => LastWriteResult;

        public bool CanOpenExclusive(string path) => CanOpenResult;
    }
}
