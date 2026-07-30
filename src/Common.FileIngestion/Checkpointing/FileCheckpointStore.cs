using System.Text.Json;

namespace Common.FileIngestion.Checkpointing;

/// <summary>
/// File-based <see cref="ICheckpointStore"/>. Persists one watermark JSON file per source key in a
/// configured durable directory. The temp file is flushed to disk (fsync) before an atomic rename, so
/// neither a process crash nor a power loss can leave a torn or unflushed watermark at the final path.
/// (Directory-entry durability after the rename is filesystem-dependent and not portably exposed by
/// .NET; on a journaled volume the rename is itself durable.) Suitable for a mounted durable volume.
/// </summary>
public sealed class FileCheckpointStore : ICheckpointStore
{
    private const string WatermarkExtension = ".watermark.json";
    private const string TempExtension = ".tmp";
    private readonly string _directory;

    /// <summary>Creates a store rooted at a durable directory (created if missing).</summary>
    /// <param name="directory">Durable checkpoint directory; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is blank.</exception>
    public FileCheckpointStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _directory = directory;
    }

    /// <inheritdoc />
    public async Task<Watermark?> LoadAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var path = PathFor(sourceKey);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Watermark>(json);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Watermark watermark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watermark);

        var path = PathFor(watermark.SourceKey);
        var temp = path + TempExtension;
        var json = JsonSerializer.SerializeToUtf8Bytes(watermark);

        // Write then fsync the temp file so its bytes are durably on disk before the rename; rename
        // atomicity alone does not guarantee the content survived a power loss.
        var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <inheritdoc />
    public Task ClearAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var path = PathFor(sourceKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);

        if (sourceKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Source key contains invalid path characters.", nameof(sourceKey));
        }

        return Path.Combine(_directory, sourceKey + WatermarkExtension);
    }
}
