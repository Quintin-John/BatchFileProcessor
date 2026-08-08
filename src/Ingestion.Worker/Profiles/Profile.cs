namespace Ingestion.Worker.Profiles;

/// <summary>
/// One ingestion profile: the operational binding of a folder to how its files are parsed and where their
/// messages go. Routing data only — folders, the layout paths (parsing/mapping lives in the layouts
/// themselves), the record format, completion detection, routing targets, and batch limits. Carries no parsing/business
/// logic; the engine reads these as configuration. Validated on construction so a bad profile fails fast.
/// </summary>
internal sealed record Profile
{
    /// <summary>Unique profile identity (provenance + checkpoint namespace).</summary>
    public string Name { get; }

    /// <summary>The four working directories files move through.</summary>
    public ProfileFolders Folders { get; }

    /// <summary>
    /// The layout definitions that may parse this profile's records; at least one.
    /// <para>
    /// Several are allowed because one folder can receive more than one version of a format. Which one a
    /// given file belongs to is decided per file by the format, not declared here — this only says which
    /// are candidates.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LayoutPaths { get; }

    /// <summary>The file format: how this profile's layout is loaded and how its records are framed and mapped.</summary>
    public IRecordFormat Format { get; }

    /// <summary>How a fully-written file is detected before it is claimed.</summary>
    public CompletionSettings Completion { get; }

    /// <summary>Where published batches and rejected records go.</summary>
    public RoutingTargets Routing { get; }

    /// <summary>Per-batch record and content-byte limits.</summary>
    public BatchLimits Batch { get; }

    /// <summary>Creates a validated profile.</summary>
    /// <param name="name">Profile name; required, non-blank.</param>
    /// <param name="folders">Working directories; required.</param>
    /// <param name="layoutPaths">Candidate layout YAML paths; required, at least one, non-blank, no duplicates.</param>
    /// <param name="format">Record format; required.</param>
    /// <param name="completion">Completion settings; required.</param>
    /// <param name="routing">Routing targets; required.</param>
    /// <param name="batch">Batch limits; required.</param>
    /// <exception cref="ArgumentException">A string is blank, no layout is declared, or a layout path repeats.</exception>
    /// <exception cref="ArgumentNullException">A required reference argument is null.</exception>
    public Profile(
        string name,
        ProfileFolders folders,
        IReadOnlyList<string> layoutPaths,
        IRecordFormat format,
        CompletionSettings completion,
        RoutingTargets routing,
        BatchLimits batch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(layoutPaths);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(batch);

        if (layoutPaths.Count == 0)
        {
            throw new ArgumentException("A profile must declare at least one layout.", nameof(layoutPaths));
        }

        // A repeat would make the same layout compete with itself, so no file could ever fit exactly one.
        var seen = new HashSet<string>(layoutPaths.Count, StringComparer.Ordinal);
        foreach (var path in layoutPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A layout path must not be blank.", nameof(layoutPaths));
            }

            if (!seen.Add(path))
            {
                throw new ArgumentException($"Duplicate layout path '{path}'.", nameof(layoutPaths));
            }
        }

        Name = name;
        Folders = folders;
        LayoutPaths = new List<string>(layoutPaths).AsReadOnly();
        Format = format;
        Completion = completion;
        Routing = routing;
        Batch = batch;
    }
}
