namespace Common.FileIngestion.Profiles;

/// <summary>
/// A soft-coded ingestion profile: which files it matches, how they're framed, which layout maps
/// them, and where their messages go. Selected per dropped file by <see cref="IProfileResolver"/>.
/// </summary>
public sealed record Profile
{
    /// <summary>Unique profile identifier.</summary>
    public string Id { get; }

    /// <summary>Path glob that selects files for this profile (e.g. <c>**/g266*</c>).</summary>
    public string Match { get; }

    /// <summary>How records are framed.</summary>
    public IngestionFormat Format { get; }

    /// <summary>Path to the layout YAML that maps records for this profile.</summary>
    public string LayoutPath { get; }

    /// <summary>Destination the profile's messages are published to.</summary>
    public string Destination { get; }

    /// <summary>Creates a validated profile.</summary>
    /// <param name="id">Unique id; required, non-blank.</param>
    /// <param name="match">Path glob; required, non-blank.</param>
    /// <param name="format">Record framing.</param>
    /// <param name="layoutPath">Layout YAML path; required, non-blank.</param>
    /// <param name="destination">Message destination; required, non-blank.</param>
    /// <exception cref="ArgumentException">Any string is blank.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="format"/> is not a defined value.</exception>
    public Profile(string id, string match, IngestionFormat format, string layoutPath, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(match);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (!Enum.IsDefined(format))
        {
            throw new InvalidOperationException($"Unknown format '{(int)format}'.");
        }

        Id = id;
        Match = match;
        Format = format;
        LayoutPath = layoutPath;
        Destination = destination;
    }
}
