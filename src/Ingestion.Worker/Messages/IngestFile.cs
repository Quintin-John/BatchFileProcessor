namespace Ingestion.Worker.Messages;

/// <summary>
/// In-process mediator command to ingest one claimed file. Dispatched by the worker's poll loop and
/// handled by the ingestion consumer; carries the file's identity, its claimed location, and the
/// provenance the pipeline stamps on emitted messages. It carries no layout version: which layout reads
/// the file is only settled once the dispatcher has matched one to it.
/// </summary>
/// <param name="SourceKey">Stable resume key (claimed file name).</param>
/// <param name="FileName">Original file name (provenance).</param>
/// <param name="ProcessingPath">Path to the claimed file to read.</param>
/// <param name="CorrelationId">Run correlation id (provenance).</param>
/// <param name="ProfileId">Matched profile id (provenance).</param>
public sealed record IngestFile(
    string SourceKey,
    string FileName,
    string ProcessingPath,
    string CorrelationId,
    string ProfileId);
