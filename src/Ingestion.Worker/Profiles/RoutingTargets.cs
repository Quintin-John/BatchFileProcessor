namespace Ingestion.Worker.Profiles;

/// <summary>
/// Where a profile's messages go: the <see cref="Batches"/> destination and the <see cref="Rejects"/>
/// destination (queue/topic names). Both travel together as one routing target.
/// </summary>
internal sealed record RoutingTargets
{
    /// <summary>Destination queue/topic name for published batches.</summary>
    public string Batches { get; }

    /// <summary>Destination queue/topic name for rejected records.</summary>
    public string Rejects { get; }

    /// <summary>Creates validated routing targets.</summary>
    /// <param name="batches">Batch destination name; required, non-blank.</param>
    /// <param name="rejects">Reject destination name; required, non-blank.</param>
    /// <exception cref="ArgumentException">Either name is blank.</exception>
    public RoutingTargets(string batches, string rejects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batches);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejects);
        Batches = batches;
        Rejects = rejects;
    }
}
