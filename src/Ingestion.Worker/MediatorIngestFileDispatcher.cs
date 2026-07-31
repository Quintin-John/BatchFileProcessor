using Ingestion.Worker.Messages;
using MassTransit.Mediator;

namespace Ingestion.Worker;

/// <summary>
/// <see cref="IIngestFileDispatcher"/> backed by the in-process MassTransit mediator. A thin adapter:
/// it forwards the command to the mediator and adds no behaviour of its own.
/// </summary>
internal sealed class MediatorIngestFileDispatcher : IIngestFileDispatcher
{
    private readonly IMediator _mediator;

    /// <summary>Creates the adapter.</summary>
    /// <param name="mediator">The mediator to forward to; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mediator"/> is null.</exception>
    public MediatorIngestFileDispatcher(IMediator mediator)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        _mediator = mediator;
    }

    /// <inheritdoc />
    public Task DispatchAsync(IngestFile command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _mediator.Send(command, cancellationToken);
    }
}
