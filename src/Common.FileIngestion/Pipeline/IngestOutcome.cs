namespace Common.FileIngestion.Pipeline;

/// <summary>Summary of a completed file ingest.</summary>
/// <param name="FileId">Content hash of the file (uppercase hex).</param>
/// <param name="RecordsAccepted">Records parsed and published in a batch.</param>
/// <param name="RecordsRejected">Records quarantined to the reject queue.</param>
/// <param name="BatchesPublished">Batches confirmed by the broker.</param>
public sealed record IngestOutcome(string FileId, long RecordsAccepted, long RecordsRejected, long BatchesPublished);
