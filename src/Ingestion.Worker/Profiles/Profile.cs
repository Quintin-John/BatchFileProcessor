namespace Ingestion.Worker.Profiles;

/// <summary>
/// One ingestion profile: the operational binding of a folder to how its files are parsed and where their
/// messages go. Routing data only — folders, the layout path (parsing/mapping lives in the layout YAML),
/// the record format, completion detection, routing targets, and batch limits. Carries no parsing/business
/// logic; the engine reads these as configuration. Validated on construction so a bad profile fails fast.
/// </summary>
internal sealed record Profile
{
    /// <summary>Unique profile identity (provenance + checkpoint namespace).</summary>
    public string Name { get; }

    /// <summary>The four working directories files move through.</summary>
    public ProfileFolders Folders { get; }

    /// <summary>Path to the layout YAML that parses/maps this profile's records.</summary>
    public string LayoutPath { get; }

    /// <summary>Framing format, selecting the record-parser strategy.</summary>
    public RecordFormat Format { get; }

    /// <summary>How a fully-written file is detected before it is claimed.</summary>
    public CompletionSettings Completion { get; }

    /// <summary>Where published batches and rejected records go.</summary>
    public RoutingTargets Routing { get; }

    /// <summary>Per-batch record and content-byte limits.</summary>
    public BatchLimits Batch { get; }

    /// <summary>Creates a validated profile.</summary>
    /// <param name="name">Profile name; required, non-blank.</param>
    /// <param name="folders">Working directories; required.</param>
    /// <param name="layoutPath">Layout YAML path; required, non-blank.</param>
    /// <param name="format">Record format; must be a defined value.</param>
    /// <param name="completion">Completion settings; required.</param>
    /// <param name="routing">Routing targets; required.</param>
    /// <param name="batch">Batch limits; required.</param>
    /// <exception cref="ArgumentException">A string is blank, or <paramref name="format"/> is undefined.</exception>
    /// <exception cref="ArgumentNullException">A required reference argument is null.</exception>
    public Profile(
        string name,
        ProfileFolders folders,
        string layoutPath,
        RecordFormat format,
        CompletionSettings completion,
        RoutingTargets routing,
        BatchLimits batch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutPath);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentException($"Undefined record format '{format}'.", nameof(format));
        }

        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(batch);

        Name = name;
        Folders = folders;
        LayoutPath = layoutPath;
        Format = format;
        Completion = completion;
        Routing = routing;
        Batch = batch;
    }
}
