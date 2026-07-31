using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Transport-agnostic send-retry decorator for <see cref="IMessagePublisher"/>. A producer only ever sees
/// acks (the send task completes), naks, or a transport failure (the send task faults). On a fault this
/// retries the publish with incremental backoff up to <see cref="MessagingResilienceOptions.RetryLimit"/>
/// attempts; once they are exhausted the last fault propagates (fail-closed) so the pipeline faults the run
/// and the file is quarantined. Cancellation is never a retryable fault. Because it sits <em>above</em> the
/// transport adapter (dependency inversion), the same retry applies to RabbitMQ, Kafka, Azure Service Bus,
/// or any other <see cref="IMessagePublisher"/>.
/// </summary>
public sealed class RetryingMessagePublisher : IMessagePublisher
{
    private readonly IMessagePublisher _inner;
    private readonly MessagingResilienceOptions _resilience;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the decorator over an inner publisher.</summary>
    /// <param name="inner">The transport publisher to retry; required.</param>
    /// <param name="resilience">Retry policy; validated.</param>
    /// <param name="timeProvider">Clock for backoff delays; required.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resilience"/> is invalid.</exception>
    public RetryingMessagePublisher(IMessagePublisher inner, MessagingResilienceOptions resilience, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(resilience);
        ArgumentNullException.ThrowIfNull(timeProvider);
        resilience.Validate();

        _inner = inner;
        _resilience = resilience;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task PublishBatchAsync(IngestBatchMessage batch, string destination, CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(ct => _inner.PublishBatchAsync(batch, destination, ct), cancellationToken);

    /// <inheritdoc />
    public Task PublishRejectAsync(RejectMessage reject, string destination, CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(ct => _inner.PublishRejectAsync(reject, destination, ct), cancellationToken);

    private async Task ExecuteWithRetryAsync(Func<CancellationToken, Task> publish, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await publish(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw; // shutdown / caller cancellation is never a retryable send fault
            }
#pragma warning disable CA1031 // any transport send fault is retryable up to the bounded limit, then propagates
            catch (Exception) when (attempt < _resilience.RetryLimit)
#pragma warning restore CA1031
            {
                await Task.Delay(BackoffFor(attempt), _timeProvider, cancellationToken).ConfigureAwait(false);
                attempt++;
            }
        }
    }

    /// <summary>Incremental backoff for a zero-based retry attempt: initial + attempt × increment.</summary>
    internal TimeSpan BackoffFor(int attempt) =>
        _resilience.RetryInitialInterval + (_resilience.RetryIntervalIncrement * attempt);
}
