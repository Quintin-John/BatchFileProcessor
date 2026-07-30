namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Batching limits for the ingestion pipeline. Set <see cref="MaxContentBytesPerBatch"/> below the
/// transport's message-size limit (with margin for envelope overhead).
/// </summary>
public sealed record IngestionOptions
{
    /// <summary>Maximum records per batch.</summary>
    public int MaxRecordsPerBatch { get; }

    /// <summary>Maximum estimated content bytes per batch.</summary>
    public int MaxContentBytesPerBatch { get; }

    /// <summary>Creates validated options.</summary>
    /// <param name="maxRecordsPerBatch">Max records per batch; at least 1.</param>
    /// <param name="maxContentBytesPerBatch">Max content bytes per batch; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either limit is less than 1.</exception>
    public IngestionOptions(int maxRecordsPerBatch, int maxContentBytesPerBatch)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRecordsPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxContentBytesPerBatch, 1);

        MaxRecordsPerBatch = maxRecordsPerBatch;
        MaxContentBytesPerBatch = maxContentBytesPerBatch;
    }
}
