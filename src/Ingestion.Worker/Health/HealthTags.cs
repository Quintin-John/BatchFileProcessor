namespace Ingestion.Worker.Health;

/// <summary>
/// Health-check tag vocabulary, defined once so the registration tags and the endpoint-mapping
/// predicates cannot drift. A predicate that matched no checks would report Healthy (200) while
/// checking nothing, so both probes must key on the same, distinct tags.
/// </summary>
internal static class HealthTags
{
    /// <summary>Tag for liveness checks (the <c>/health/live</c> probe).</summary>
    public const string Live = "live";

    /// <summary>Tag for readiness checks (the <c>/health/ready</c> probe).</summary>
    public const string Ready = "ready";
}
