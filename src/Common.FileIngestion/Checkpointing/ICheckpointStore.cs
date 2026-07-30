namespace Common.FileIngestion.Checkpointing;

/// <summary>
/// Durable store for the per-file high-water mark. The file-based implementation is the default;
/// a blob/DB implementation can be added behind this seam without touching the pipeline.
/// </summary>
public interface ICheckpointStore
{
    /// <summary>Loads the watermark for a file, or null if none has been saved.</summary>
    /// <param name="fileId">File identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Watermark?> LoadAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>Durably and atomically persists a watermark.</summary>
    /// <param name="watermark">The watermark to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(Watermark watermark, CancellationToken cancellationToken);

    /// <summary>Removes the watermark for a file (called when a file completes successfully).</summary>
    /// <param name="fileId">File identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearAsync(string fileId, CancellationToken cancellationToken);
}
