using Common.FileIngestion.Abstractions;

namespace Common.FileIngestion.Sources;

/// <summary>
/// Completion guard for producers we do not control (no atomic-rename or sentinel): a file is complete
/// once its size and last-write timestamp have been unchanged for a configured quiet period and it opens
/// with no sharing (no writer still holds it). Stateful across polls (it remembers the last observation
/// per path) and not thread-safe — one instance per single-threaded poll loop. Time is injected for
/// deterministic testing.
/// </summary>
public sealed class StableSizeCompletionGuard : ICompletionGuard
{
    private readonly TimeSpan _quietPeriod;
    private readonly TimeProvider _timeProvider;
    private readonly IFileProbe _probe;
    private readonly Dictionary<string, Observation> _observations = new(StringComparer.Ordinal);

    /// <summary>Creates a guard over the real filesystem.</summary>
    /// <param name="quietPeriod">How long size/last-write must be unchanged before complete; must be positive.</param>
    /// <param name="timeProvider">Clock source; required.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quietPeriod"/> is not positive.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public StableSizeCompletionGuard(TimeSpan quietPeriod, TimeProvider timeProvider)
        : this(quietPeriod, timeProvider, new FileSystemProbe())
    {
    }

    internal StableSizeCompletionGuard(TimeSpan quietPeriod, TimeProvider timeProvider, IFileProbe probe)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quietPeriod, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(probe);

        _quietPeriod = quietPeriod;
        _timeProvider = timeProvider;
        _probe = probe;
    }

    /// <inheritdoc />
    public bool IsComplete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_probe.Exists(path))
        {
            _observations.Remove(path);
            return false;
        }

        var length = _probe.Length(path);
        var lastWrite = _probe.LastWriteTimeUtc(path);
        var now = _timeProvider.GetUtcNow();

        if (!_observations.TryGetValue(path, out var previous) ||
            previous.Length != length || previous.LastWrite != lastWrite)
        {
            // First sighting, or the file changed since the last poll — (re)start the quiet period.
            _observations[path] = new Observation(length, lastWrite, now);
            return false;
        }

        if (now - previous.StableSince < _quietPeriod)
        {
            return false; // unchanged, but not quiet long enough yet
        }

        if (!_probe.CanOpenExclusive(path))
        {
            return false; // size is stable but a writer still holds the file open
        }

        _observations.Remove(path); // complete — it will be claimed and moved out of the folder
        return true;
    }

    private readonly record struct Observation(long Length, DateTimeOffset LastWrite, DateTimeOffset StableSince);
}
