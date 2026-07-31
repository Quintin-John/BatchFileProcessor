using Ingestion.Worker.Messages;

namespace Ingestion.Worker;

/// <summary>
/// Dispatches one <see cref="IngestFile"/> command for processing. A narrow seam over the mediator so
/// the worker depends only on the single operation it uses (rather than the whole mediator surface),
/// and so its cancellation handling is deterministically testable.
/// </summary>
internal interface IIngestFileDispatcher
{
    /// <summary>Dispatches the command and completes when processing finishes.</summary>
    /// <param name="command">The command to dispatch; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync(IngestFile command, CancellationToken cancellationToken);
}
