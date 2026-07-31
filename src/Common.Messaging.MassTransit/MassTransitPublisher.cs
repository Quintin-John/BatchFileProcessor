using MassTransit;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Publishes messages to a named destination via an addressed MassTransit send (<c>queue:{name}</c>), so
/// each profile routes to its own queue/topic. With publisher confirms enabled on the transport, the send
/// task completes only when the broker has accepted the message, so any failure faults the task (fail-closed).
/// </summary>
public sealed class MassTransitPublisher : IMessagePublisher
{
    /// <summary>Header carrying the run correlation id for downstream trace continuity.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly IBus _bus;

    /// <summary>Creates the publisher.</summary>
    /// <param name="bus">The MassTransit bus (resolves send endpoints); required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> is null.</exception>
    public MassTransitPublisher(IBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
    }

    /// <inheritdoc />
    public Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return SendAsync(batch, batch.Provenance.CorrelationId, destination, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reject);
        return SendAsync(reject, reject.Provenance.CorrelationId, destination, cancellationToken);
    }

    private async Task SendAsync<T>(T message, string correlationId, string destination, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var endpoint = await _bus.GetSendEndpoint(DestinationAddress(destination)).ConfigureAwait(false);
        await endpoint.Send(message, context => context.Headers.Set(CorrelationIdHeader, correlationId), cancellationToken)
            .ConfigureAwait(false);
    }

    private static Uri DestinationAddress(string destination) => new("queue:" + destination);
}
