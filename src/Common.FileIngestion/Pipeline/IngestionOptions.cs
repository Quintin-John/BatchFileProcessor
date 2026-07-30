namespace Common.FileIngestion.Pipeline;

/// <summary>
/// Pipeline limits for ingestion. Set <see cref="MaxContentBytesPerBatch"/> below the transport's
/// message-size limit (with margin for envelope overhead). <see cref="BatchChannelCapacity"/> bounds
/// how many sealed batches can queue between the read/map stage and the publisher(s) — capping
/// in-flight memory regardless of file size (design §3.1) and applying backpressure to the reader.
/// </summary>
public sealed record IngestionOptions
{
    /// <summary>Maximum records per batch.</summary>
    public int MaxRecordsPerBatch { get; }

    /// <summary>Maximum estimated content bytes per batch.</summary>
    public int MaxContentBytesPerBatch { get; }

    /// <summary>Capacity of the bounded batch channel between reader and publisher(s).</summary>
    public int BatchChannelCapacity { get; }

    /// <summary>Creates validated options.</summary>
    /// <param name="maxRecordsPerBatch">Max records per batch; at least 1.</param>
    /// <param name="maxContentBytesPerBatch">Max content bytes per batch; at least 1.</param>
    /// <param name="batchChannelCapacity">Bounded batch-channel capacity; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any value is less than 1.</exception>
    public IngestionOptions(int maxRecordsPerBatch, int maxContentBytesPerBatch, int batchChannelCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRecordsPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxContentBytesPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchChannelCapacity, 1);

        MaxRecordsPerBatch = maxRecordsPerBatch;
        MaxContentBytesPerBatch = maxContentBytesPerBatch;
        BatchChannelCapacity = batchChannelCapacity;
    }
}
