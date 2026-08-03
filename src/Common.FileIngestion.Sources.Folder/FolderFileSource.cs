using Common.FileIngestion.Abstractions;
using Microsoft.Extensions.Logging;

namespace Common.FileIngestion.Sources;

/// <summary>
/// File-system <see cref="IFileSource"/> over four explicit directories: <c>incoming</c> (arrivals),
/// <c>processing</c> (claimed), <c>done</c> (completed archive), and <c>failed</c> (quarantine). A file is
/// claimed only once a <see cref="ICompletionGuard"/> reports it fully written, then atomic-renamed into
/// <c>processing</c> (same-volume), so it is claimed by exactly one caller and stays immutable while
/// processed. Files left in <c>processing</c> after a crash are re-offered by <see cref="RecoverOrphans"/>.
/// Directories are supplied explicitly so each profile owns its own set.
/// </summary>
public sealed partial class FolderFileSource : IFileSource, IDisposable
{
    private const string LockFileName = ".ingestion.lock";

    private readonly string _incoming;
    private readonly string _processing;
    private readonly string _done;
    private readonly string _failed;
    private readonly ICompletionGuard _completionGuard;
    private readonly FileStream _ownershipLock;
    private readonly ILogger<FolderFileSource> _logger;

    /// <summary>
    /// Creates the source, creating the four directories if missing and taking exclusive ownership (a lock
    /// file in <c>processing</c>). Orphan recovery re-offers files in <c>processing</c>, which is only safe
    /// if exactly one instance owns them — so a second instance on the same directories fails closed here
    /// rather than stealing a file the first is actively processing.
    /// </summary>
    /// <param name="incoming">Arrivals directory; required, non-blank.</param>
    /// <param name="processing">Claimed directory; required, non-blank.</param>
    /// <param name="done">Completed archive directory; required, non-blank.</param>
    /// <param name="failed">Quarantine directory; required, non-blank.</param>
    /// <param name="completionGuard">Decides when a file is fully written; required.</param>
    /// <param name="logger">Logger for skipped-claim diagnostics; required.</param>
    /// <exception cref="ArgumentException">Any directory is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="completionGuard"/> or <paramref name="logger"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Another instance already owns the directories.</exception>
    public FolderFileSource(
        string incoming,
        string processing,
        string done,
        string failed,
        ICompletionGuard completionGuard,
        ILogger<FolderFileSource> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        ArgumentException.ThrowIfNullOrWhiteSpace(processing);
        ArgumentException.ThrowIfNullOrWhiteSpace(done);
        ArgumentException.ThrowIfNullOrWhiteSpace(failed);
        ArgumentNullException.ThrowIfNull(completionGuard);
        ArgumentNullException.ThrowIfNull(logger);

        _incoming = incoming;
        _processing = processing;
        _done = done;
        _failed = failed;
        _completionGuard = completionGuard;
        _logger = logger;

        Directory.CreateDirectory(_incoming);
        Directory.CreateDirectory(_processing);
        Directory.CreateDirectory(_done);
        Directory.CreateDirectory(_failed);

        _ownershipLock = AcquireOwnership(_processing);
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

            // The completion probe and the claim move are both filesystem operations on a file an uncontrolled
            // producer may delete, rename, or lock between our enumeration and these calls. Any such failure
            // must skip only this file — never fault the poll loop, which would crash the worker host and stall
            // all ingestion until restart. So the whole per-file body is guarded (not just the move).
            try
            {
                // Never claim a file the producer may still be writing; leave it for a later poll.
                if (!_completionGuard.IsComplete(path))
                {
                    continue;
                }

                File.Move(path, destination); // atomic claim; throws if already claimed
                claimed.Add(new ClaimedFile(name, destination));
            }
            catch (IOException ex)
            {
                // A same-name file already in processing (an orphan not yet cleared) is expected in the
                // recurring-file model. Otherwise the arrival vanished/was locked mid-poll, or the move
                // failed (disk full, cross-volume). Either way, skip this file so one bad arrival never
                // stalls the batch.
                if (File.Exists(destination))
                {
                    LogClaimCollision(name);
                }
                else
                {
                    LogClaimFailed(ex, name);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // Transient lock / permission (e.g. an AV scanner or the producer's own handle); retry next poll.
                LogClaimFailed(ex, name);
            }
        }

        return claimed;
    }

    /// <inheritdoc />
    public void Complete(ClaimedFile file) => MoveTo(file, _done);

    /// <inheritdoc />
    public void Fail(ClaimedFile file) => MoveTo(file, _failed);

    /// <summary>Releases exclusive ownership.</summary>
    public void Dispose() => _ownershipLock.Dispose();

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Skipped claiming {File}: a same-name file is already in processing; leaving it for a later poll.")]
    private partial void LogClaimCollision(string file);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Could not claim {File}; skipping it this poll.")]
    private partial void LogClaimFailed(Exception exception, string file);

    private static FileStream AcquireOwnership(string directory)
    {
        var lockPath = Path.Combine(directory, LockFileName);
        try
        {
            // Exclusive handle held for this source's lifetime; a second instance's open fails while held.
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Another ingestion instance already owns '{directory}'; the folder source is single-instance.",
                ex);
        }
    }

    private static List<ClaimedFile> Enumerate(string directory) =>
        Directory.EnumerateFiles(directory)
            // The ownership lock lives in processing; it is not an orphaned data file.
            .Where(p => !string.Equals(Path.GetFileName(p), LockFileName, StringComparison.Ordinal))
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
