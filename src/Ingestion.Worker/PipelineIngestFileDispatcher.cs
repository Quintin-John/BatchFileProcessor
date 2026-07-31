using Common.FileIngestion.Pipeline;
using Ingestion.Worker.Messages;

namespace Ingestion.Worker;

/// <summary>
/// <see cref="IIngestFileDispatcher"/> that runs a claimed file through a profile's own
/// <see cref="FileIngestionPipeline"/> directly — no mediator. It opens the claimed file and delegates to
/// the pipeline, propagating only what the pipeline throws. Record-level parse failures are not surfaced
/// here: the pipeline routes each bad record to the reject queue and the file still completes. Only a
/// file-level fault (integrity/publish/checkpoint failure) propagates, which the worker then quarantines.
/// One dispatcher per profile, bound to that profile's pipeline.
/// </summary>
internal sealed class PipelineIngestFileDispatcher : IIngestFileDispatcher
{
    private readonly FileIngestionPipeline _pipeline;

    /// <summary>Creates the dispatcher for a profile's pipeline.</summary>
    /// <param name="pipeline">The profile's pipeline; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeline"/> is null.</exception>
    public PipelineIngestFileDispatcher(FileIngestionPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public Task DispatchAsync(IngestFile command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = new IngestRequest(
            command.SourceKey,
            command.FileName,
            command.CorrelationId,
            command.ProfileId,
            command.LayoutVersion,
            () => File.OpenRead(command.ProcessingPath));

        return _pipeline.IngestAsync(request, cancellationToken);
    }
}
