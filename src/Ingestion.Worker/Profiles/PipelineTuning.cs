namespace Ingestion.Worker.Profiles;

/// <summary>
/// Shared pipeline tuning applied to every profile: the ingest channel capacity, the number of concurrent
/// publishers, and the outstanding-confirms window. Per-batch limits are per-profile (in the layout's
/// profile); these are infra tuning from appsettings. All must be at least 1.
/// </summary>
internal sealed record PipelineTuning
{
    /// <summary>Bounded ingest channel capacity.</summary>
    public int BatchChannelCapacity { get; }

    /// <summary>Number of concurrent publisher tasks.</summary>
    public int PublisherConcurrency { get; }

    /// <summary>Maximum unconfirmed batches in flight.</summary>
    public int PublisherConfirmWindow { get; }

    /// <summary>Creates validated tuning.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Any value is less than 1.</exception>
    public PipelineTuning(int batchChannelCapacity, int publisherConcurrency, int publisherConfirmWindow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchChannelCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(publisherConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(publisherConfirmWindow, 1);

        BatchChannelCapacity = batchChannelCapacity;
        PublisherConcurrency = publisherConcurrency;
        PublisherConfirmWindow = publisherConfirmWindow;
    }
}
