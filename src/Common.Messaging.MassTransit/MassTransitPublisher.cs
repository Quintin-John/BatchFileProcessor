using MassTransit;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Publishes batches through a MassTransit <see cref="IPublishEndpoint"/>. With publisher confirms
/// enabled on the transport, the publish task completes only when the broker has accepted the
/// message, so any failure faults the task (fail-closed).
/// </summary>
public sealed class MassTransitPublisher : IMessagePublisher
{
    /// <summary>Header carrying the run correlation id for downstream trace continuity.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly IPublishEndpoint _publishEndpoint;

    /// <summary>Creates the publisher.</summary>
    /// <param name="publishEndpoint">MassTransit publish endpoint (typically the bus).</param>
    /// <exception cref="ArgumentNullException"><paramref name="publishEndpoint"/> is null.</exception>
    public MassTransitPublisher(IPublishEndpoint publishEndpoint)
    {
        ArgumentNullException.ThrowIfNull(publishEndpoint);
        _publishEndpoint = publishEndpoint;
    }

    /// <inheritdoc />
    public Task PublishBatchAsync(IngestBatchMessage batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return _publishEndpoint.Publish(
            batch,
            context => context.Headers.Set(CorrelationIdHeader, batch.Provenance.CorrelationId),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishRejectAsync(RejectMessage reject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reject);

        return _publishEndpoint.Publish(
            reject,
            context => context.Headers.Set(CorrelationIdHeader, reject.Provenance.CorrelationId),
            cancellationToken);
    }
}
