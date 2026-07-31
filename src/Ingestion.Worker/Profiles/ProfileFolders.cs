namespace Ingestion.Worker.Profiles;

/// <summary>
/// The four working directories a profile moves files through: arrivals (<see cref="Incoming"/>),
/// claimed (<see cref="Processing"/>), completed archive (<see cref="Done"/>), and quarantine
/// (<see cref="Failed"/>). The four travel together and must be distinct — the same path for two roles
/// would let a file collide with itself — so they are one validated value rather than four loose strings.
/// </summary>
internal sealed record ProfileFolders
{
    /// <summary>Directory new files are dropped into.</summary>
    public string Incoming { get; }

    /// <summary>Directory a claimed file is moved to while processing.</summary>
    public string Processing { get; }

    /// <summary>Archive directory a completed file is moved to.</summary>
    public string Done { get; }

    /// <summary>Quarantine directory a failed file is moved to.</summary>
    public string Failed { get; }

    /// <summary>Creates validated folders.</summary>
    /// <param name="incoming">Arrivals directory; required, non-blank.</param>
    /// <param name="processing">Claimed directory; required, non-blank.</param>
    /// <param name="done">Completed archive directory; required, non-blank.</param>
    /// <param name="failed">Quarantine directory; required, non-blank.</param>
    /// <exception cref="ArgumentException">Any directory is blank, or two roles share the same path.</exception>
    public ProfileFolders(string incoming, string processing, string done, string failed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        ArgumentException.ThrowIfNullOrWhiteSpace(processing);
        ArgumentException.ThrowIfNullOrWhiteSpace(done);
        ArgumentException.ThrowIfNullOrWhiteSpace(failed);

        var distinct = new HashSet<string>(StringComparer.Ordinal) { incoming, processing, done, failed };
        if (distinct.Count != 4)
        {
            throw new ArgumentException("Incoming, processing, done, and failed directories must be distinct.");
        }

        Incoming = incoming;
        Processing = processing;
        Done = done;
        Failed = failed;
    }
}
