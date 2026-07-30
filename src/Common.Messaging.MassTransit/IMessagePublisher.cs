using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Publishes batch messages to the transport. The returned task completes only once the broker
/// has accepted the message (publisher confirms), so a failure faults the task — fail-closed.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>Publishes a batch, completing when the broker confirms acceptance.</summary>
    /// <param name="batch">The batch to publish; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is null.</exception>
    Task PublishBatchAsync(IngestBatchMessage batch, CancellationToken cancellationToken);
}
