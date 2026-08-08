namespace Ingestion.Worker;

/// <summary>
/// Runtime settings for the folder ingestion worker. Bound from configuration in the composition
/// root and validated on construction so a misconfiguration fails fast at startup.
/// </summary>
public sealed record WorkerOptions
{
    /// <summary>Id of the profile applied to claimed files (message provenance).</summary>
    public string ProfileId { get; }

    /// <summary>Delay between polls of the incoming directory.</summary>
    public TimeSpan PollInterval { get; }

    /// <summary>Creates validated options.</summary>
    /// <param name="profileId">Profile id; required, non-blank.</param>
    /// <param name="pollInterval">Poll delay; must be positive.</param>
    /// <exception cref="ArgumentException">A string argument is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pollInterval"/> is not positive.</exception>
    public WorkerOptions(string profileId, TimeSpan pollInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero);

        ProfileId = profileId;
        PollInterval = pollInterval;
    }
}
