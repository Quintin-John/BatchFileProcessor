namespace Common.Messaging.MassTransit;

/// <summary>
/// Soft-coded resilience configuration applied to the message pipeline: retry with backoff,
/// circuit breaker, and optional rate limiting. Bound from application config.
/// </summary>
public sealed class MessagingResilienceOptions
{
    /// <summary>Number of retry attempts before a message faults.</summary>
    public int RetryLimit { get; set; } = 5;

    /// <summary>Initial retry interval.</summary>
    public TimeSpan RetryInitialInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Amount each retry interval grows by.</summary>
    public TimeSpan RetryIntervalIncrement { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Whether to add jitter to retry intervals.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>Failure percentage (0..100) at which the circuit breaker trips.</summary>
    public int CircuitBreakerTripThreshold { get; set; } = 15;

    /// <summary>Minimum number of messages in the tracking window before the breaker can trip.</summary>
    public int CircuitBreakerActiveThreshold { get; set; } = 10;

    /// <summary>How long the breaker stays open before probing again.</summary>
    public TimeSpan CircuitBreakerResetInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum messages per interval, or 0 to disable rate limiting.</summary>
    public int RateLimit { get; set; }

    /// <summary>Validates the options. Fail-closed on invalid configuration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A numeric or interval value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RetryLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(RetryInitialInterval.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(RetryIntervalIncrement.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(CircuitBreakerActiveThreshold);
        ArgumentOutOfRangeException.ThrowIfNegative(CircuitBreakerTripThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(CircuitBreakerTripThreshold, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(CircuitBreakerResetInterval.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(RateLimit);
    }
}
