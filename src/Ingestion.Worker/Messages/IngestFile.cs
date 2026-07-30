namespace Ingestion.Worker.Messages;

/// <summary>
/// In-process mediator command to ingest one claimed file. Dispatched by the worker's poll loop and
/// handled by the ingestion consumer; carries the file's identity, its claimed location, and the
/// provenance the pipeline stamps on emitted messages.
/// </summary>
/// <param name="SourceKey">Stable resume key (claimed file name).</param>
/// <param name="FileName">Original file name (provenance).</param>
/// <param name="ProcessingPath">Path to the claimed file to read.</param>
/// <param name="CorrelationId">Run correlation id (provenance).</param>
/// <param name="ProfileId">Matched profile id (provenance).</param>
/// <param name="LayoutVersion">Layout version (provenance).</param>
public sealed record IngestFile(
    string SourceKey,
    string FileName,
    string ProcessingPath,
    string CorrelationId,
    string ProfileId,
    string LayoutVersion);
