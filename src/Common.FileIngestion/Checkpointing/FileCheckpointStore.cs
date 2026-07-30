using System.Text.Json;

namespace Common.FileIngestion.Checkpointing;

/// <summary>
/// File-based <see cref="ICheckpointStore"/>. Persists one watermark JSON file per file id in a
/// configured durable directory, written atomically (temp file then rename) so a crash mid-write
/// never leaves a corrupt watermark. Suitable for a mounted durable volume.
/// </summary>
public sealed class FileCheckpointStore : ICheckpointStore
{
    private const string WatermarkExtension = ".watermark.json";
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
    public async Task<Watermark?> LoadAsync(string fileId, CancellationToken cancellationToken)
    {
        var path = PathFor(fileId);
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

        var path = PathFor(watermark.FileId);
        var temp = path + ".tmp";
        var json = JsonSerializer.SerializeToUtf8Bytes(watermark);

        await File.WriteAllBytesAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    /// <inheritdoc />
    public Task ClearAsync(string fileId, CancellationToken cancellationToken)
    {
        var path = PathFor(fileId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string fileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        if (fileId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("File id contains invalid path characters.", nameof(fileId));
        }

        return Path.Combine(_directory, fileId + WatermarkExtension);
    }
}
