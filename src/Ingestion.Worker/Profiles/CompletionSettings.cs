namespace Ingestion.Worker.Profiles;

/// <summary>
/// How a profile detects a fully-written file: the guard <see cref="Mode"/>, the <see cref="QuietPeriod"/>
/// a file's size must be unchanged before it is considered complete, and the <see cref="PollInterval"/>
/// between size checks. Validated on construction so a misconfigured profile fails fast at load.
/// </summary>
internal sealed record CompletionSettings
{
    /// <summary>The completion-detection strategy.</summary>
    public CompletionMode Mode { get; }

    /// <summary>How long a file's size must stay unchanged before it is deemed complete.</summary>
    public TimeSpan QuietPeriod { get; }

    /// <summary>Delay between successive size checks while waiting for the quiet period.</summary>
    public TimeSpan PollInterval { get; }

    /// <summary>Creates validated completion settings.</summary>
    /// <param name="mode">Completion-detection mode; must be a defined value.</param>
    /// <param name="quietPeriod">Quiet period; must be positive.</param>
    /// <param name="pollInterval">Poll interval; must be positive.</param>
    /// <exception cref="ArgumentException"><paramref name="mode"/> is not a defined value.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quietPeriod"/> or <paramref name="pollInterval"/> is not positive.</exception>
    public CompletionSettings(CompletionMode mode, TimeSpan quietPeriod, TimeSpan pollInterval)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentException($"Undefined completion mode '{mode}'.", nameof(mode));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quietPeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero);

        Mode = mode;
        QuietPeriod = quietPeriod;
        PollInterval = pollInterval;
    }
}
