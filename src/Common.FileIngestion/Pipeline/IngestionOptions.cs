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

    /// <summary>Number of concurrent publisher tasks draining the batch channel (design §3 fan-out).</summary>
    public int PublisherConcurrency { get; }

    /// <summary>
    /// Outstanding-confirms window (design §3.1, W): the maximum number of batches created but not yet part
    /// of the contiguous confirmed prefix. Bounds the confirm-tracking set so it can't grow with file size.
    /// </summary>
    public int PublisherConfirmWindow { get; }

    /// <summary>Creates validated options.</summary>
    /// <param name="maxRecordsPerBatch">Max records per batch; at least 1.</param>
    /// <param name="maxContentBytesPerBatch">Max content bytes per batch; at least 1.</param>
    /// <param name="batchChannelCapacity">Bounded batch-channel capacity; at least 1.</param>
    /// <param name="publisherConcurrency">Concurrent publisher tasks; at least 1.</param>
    /// <param name="publisherConfirmWindow">Outstanding-confirms window; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any value is less than 1.</exception>
    public IngestionOptions(
        int maxRecordsPerBatch,
        int maxContentBytesPerBatch,
        int batchChannelCapacity,
        int publisherConcurrency,
        int publisherConfirmWindow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRecordsPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxContentBytesPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchChannelCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(publisherConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(publisherConfirmWindow, 1);

        MaxRecordsPerBatch = maxRecordsPerBatch;
        MaxContentBytesPerBatch = maxContentBytesPerBatch;
        BatchChannelCapacity = batchChannelCapacity;
        PublisherConcurrency = publisherConcurrency;
        PublisherConfirmWindow = publisherConfirmWindow;
    }
}
