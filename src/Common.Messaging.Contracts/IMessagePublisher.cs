namespace Common.Messaging.Contracts;

/// <summary>
/// Port for publishing ingestion messages to the transport. The returned task completes only once
/// the broker has accepted the message (publisher confirms), so a failure faults the task —
/// fail-closed. Defined alongside the message contracts so producers depend on this abstraction,
/// not on any transport adapter (dependency inversion). Implemented by the MassTransit adapter.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>Publishes a batch to a named destination, completing when the broker confirms acceptance.</summary>
    /// <param name="batch">The batch to publish; required.</param>
    /// <param name="destination">Destination queue/topic name; required, non-blank.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is blank.</exception>
    Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken);

    /// <summary>Publishes a rejected record to a named destination, completing when the broker confirms acceptance.</summary>
    /// <param name="reject">The reject message to publish; required.</param>
    /// <param name="destination">Destination queue/topic name; required, non-blank.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reject"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is blank.</exception>
    Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken);
}
