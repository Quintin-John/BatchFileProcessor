namespace Common.FileIngestion.Health;

/// <summary>
/// A liveness heartbeat: the pipeline calls <see cref="Beat"/> as it makes forward progress, and a
/// probe reads <see cref="TimeSinceLastBeat"/> to detect a deadlock. Time is injected via
/// <see cref="TimeProvider"/> for deterministic testing; the last-beat timestamp is updated and read
/// atomically so producer and probe can run on different threads.
/// </summary>
public sealed class Heartbeat
{
    private readonly TimeProvider _timeProvider;
    private long _lastBeatTicks;

    /// <summary>Creates a heartbeat, seeded as beating now.</summary>
    /// <param name="timeProvider">Clock source; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public Heartbeat(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _lastBeatTicks = timeProvider.GetUtcNow().UtcTicks;
    }

    /// <summary>Records forward progress at the current time.</summary>
    public void Beat() => Interlocked.Exchange(ref _lastBeatTicks, _timeProvider.GetUtcNow().UtcTicks);

    /// <summary>Time elapsed since the last beat (never negative).</summary>
    public TimeSpan TimeSinceLastBeat
    {
        get
        {
            var delta = _timeProvider.GetUtcNow().UtcTicks - Interlocked.Read(ref _lastBeatTicks);
            return delta > 0 ? TimeSpan.FromTicks(delta) : TimeSpan.Zero;
        }
    }
}
