namespace Ingestion.Worker.Profiles;

/// <summary>
/// Per-batch limits: <see cref="MaxRecords"/> and <see cref="MaxContentBytes"/> of serialized content.
/// Both must be at least 1 and are applied together when sealing a batch, so they travel as one value.
/// </summary>
internal sealed record BatchLimits
{
    /// <summary>Maximum records per published batch.</summary>
    public int MaxRecords { get; }

    /// <summary>Maximum serialized content bytes per published batch.</summary>
    public int MaxContentBytes { get; }

    /// <summary>Creates validated batch limits.</summary>
    /// <param name="maxRecords">Max records per batch; at least 1.</param>
    /// <param name="maxContentBytes">Max content bytes per batch; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either limit is less than 1.</exception>
    public BatchLimits(int maxRecords, int maxContentBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRecords, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxContentBytes, 1);
        MaxRecords = maxRecords;
        MaxContentBytes = maxContentBytes;
    }
}
