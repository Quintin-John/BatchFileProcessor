using MassTransit;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Publishes messages to a named destination via an addressed MassTransit send (<c>queue:{name}</c>), so
/// each profile routes to its own queue/topic. With publisher confirms enabled on the transport, the send
/// task completes only when the broker has accepted the message, so any failure faults the task (fail-closed).
/// Each send stamps a deterministic envelope <c>MessageId</c> derived from the message's own id, so a replay
/// carries the same id and the broker/inbox can deduplicate it; the run correlation id is set as the native
/// envelope <c>CorrelationId</c> when it is a GUID, and always travels on the <c>X-Correlation-Id</c> header.
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
        return SendAsync(batch, batch.MessageId, batch.Provenance.CorrelationId, destination, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reject);
        return SendAsync(reject, reject.MessageId, reject.Provenance.CorrelationId, destination, cancellationToken);
    }

    private async Task SendAsync<T>(
        T message, string messageId, string correlationId, string destination, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var endpoint = await _bus.GetSendEndpoint(DestinationAddress(destination)).ConfigureAwait(false);
        await endpoint.Send(message, context =>
        {
            // Deterministic envelope id from the domain MessageId, so a replayed message carries the same id
            // and the broker / MassTransit inbox can deduplicate it (the envelope id is Guid-typed; the
            // string id itself stays in the payload).
            context.MessageId = DeterministicGuid.From(messageId);

            // Expose the run correlation id as the native envelope CorrelationId when it is a GUID (it is,
            // from the worker); the string form stays canonical on the header for non-GUID producers.
            if (Guid.TryParse(correlationId, out var correlation))
            {
                context.CorrelationId = correlation;
            }

            context.Headers.Set(CorrelationIdHeader, correlationId);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Uri DestinationAddress(string destination) => new("queue:" + destination);
}
