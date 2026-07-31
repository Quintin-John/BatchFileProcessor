using Common.FileIngestion.Abstractions;
namespace Common.FileIngestion.Sources;

/// <summary>
/// File-system <see cref="IFileSource"/> over a root with four conventional subdirectories:
/// <c>incoming</c> (arrivals), <c>processing</c> (claimed), <c>done</c> (completed), and
/// <c>failed</c> (quarantined). Claiming is an atomic same-volume rename into <c>processing</c>, so a
/// file is claimed by exactly one caller and stays immutable while processed. Files left in
/// <c>processing</c> after a crash are re-offered by <see cref="RecoverOrphans"/> and resumed from
/// their watermark.
/// </summary>
public sealed class FolderFileSource : IFileSource, IDisposable
{
    private const string IncomingDir = "incoming";
    private const string ProcessingDir = "processing";
    private const string DoneDir = "done";
    private const string FailedDir = "failed";
    private const string LockFileName = ".ingestion.lock";

    private readonly string _incoming;
    private readonly string _processing;
    private readonly string _done;
    private readonly string _failed;
    private readonly FileStream _ownershipLock;

    /// <summary>
    /// Creates the source, creating the four subdirectories if missing and taking exclusive ownership
    /// of the root. Orphan recovery re-offers files sitting in <c>processing</c>, which is only safe if
    /// exactly one instance owns the root — so a second instance on the same root fails closed here
    /// rather than stealing a file the first is actively processing.
    /// </summary>
    /// <param name="rootDirectory">Root directory; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">Another instance already owns the root.</exception>
    public FolderFileSource(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        _incoming = Path.Combine(rootDirectory, IncomingDir);
        _processing = Path.Combine(rootDirectory, ProcessingDir);
        _done = Path.Combine(rootDirectory, DoneDir);
        _failed = Path.Combine(rootDirectory, FailedDir);

        Directory.CreateDirectory(_incoming);
        Directory.CreateDirectory(_processing);
        Directory.CreateDirectory(_done);
        Directory.CreateDirectory(_failed);

        _ownershipLock = AcquireOwnership(rootDirectory);
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaimedFile> RecoverOrphans() => Enumerate(_processing);

    /// <inheritdoc />
    public IReadOnlyList<ClaimedFile> Claim()
    {
        var claimed = new List<ClaimedFile>();
        foreach (var path in Directory.EnumerateFiles(_incoming).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var destination = Path.Combine(_processing, name);
            try
            {
                File.Move(path, destination); // atomic claim; throws if already claimed
            }
            catch (IOException)
            {
                continue; // lost the race to another poller/instance — skip
            }

            claimed.Add(new ClaimedFile(name, destination));
        }

        return claimed;
    }

    /// <inheritdoc />
    public void Complete(ClaimedFile file) => MoveTo(file, _done);

    /// <inheritdoc />
    public void Fail(ClaimedFile file) => MoveTo(file, _failed);

    /// <summary>Releases exclusive ownership of the root.</summary>
    public void Dispose() => _ownershipLock.Dispose();

    private static FileStream AcquireOwnership(string rootDirectory)
    {
        var lockPath = Path.Combine(rootDirectory, LockFileName);
        try
        {
            // Exclusive handle held for this source's lifetime; a second instance's open fails while held.
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Another ingestion instance already owns '{rootDirectory}'; the folder source is single-instance.",
                ex);
        }
    }

    private static List<ClaimedFile> Enumerate(string directory) =>
        Directory.EnumerateFiles(directory)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new ClaimedFile(Path.GetFileName(p), p))
            .ToList();

    private static void MoveTo(ClaimedFile file, string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(file);
        // Never overwrite: a same-name file already in done/failed is a prior original and must be
        // preserved for audit. Disambiguate so both are kept rather than clobbering the earlier one.
        File.Move(file.ProcessingPath, CollisionFreePath(targetDirectory, file.Name));
    }

    private static string CollisionFreePath(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);
        var suffix = 0;
        while (File.Exists(candidate))
        {
            suffix++;
            candidate = Path.Combine(directory, $"{name}.{suffix}");
        }

        return candidate;
    }
}
