namespace Common.Messaging.MassTransit;

/// <summary>
/// Soft-coded resilience configuration applied to the message pipeline: retry with backoff,
/// circuit breaker, and optional rate limiting. Bound from application config.
/// </summary>
public sealed class MessagingResilienceOptions
{
    /// <summary>Upper bound for <see cref="CircuitBreakerTripThreshold"/>, expressed as a percentage.</summary>
    private const int MaxTripThresholdPercent = 100;

    /// <summary>Number of retry attempts before a message faults.</summary>
    public int RetryLimit { get; set; } = 5;

    /// <summary>Initial retry interval.</summary>
    public TimeSpan RetryInitialInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Amount each retry interval grows by.</summary>
    public TimeSpan RetryIntervalIncrement { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Failure percentage (0..100) at which the circuit breaker trips.</summary>
    public int CircuitBreakerTripThreshold { get; set; } = 15;

    /// <summary>Minimum number of messages in the tracking window before the breaker can trip.</summary>
    public int CircuitBreakerActiveThreshold { get; set; } = 10;

    /// <summary>How long the breaker stays open before probing again.</summary>
    public TimeSpan CircuitBreakerResetInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum messages per <see cref="RateLimitInterval"/>, or 0 to disable rate limiting.</summary>
    public int RateLimit { get; set; }

    /// <summary>Window over which <see cref="RateLimit"/> applies.</summary>
    public TimeSpan RateLimitInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Validates the options. Fail-closed on invalid configuration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A numeric or interval value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RetryLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(RetryInitialInterval.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(RetryIntervalIncrement.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(CircuitBreakerActiveThreshold);
        ArgumentOutOfRangeException.ThrowIfNegative(CircuitBreakerTripThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(CircuitBreakerTripThreshold, MaxTripThresholdPercent);
        ArgumentOutOfRangeException.ThrowIfNegative(CircuitBreakerResetInterval.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(RateLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(RateLimitInterval.Ticks);

        // Coupled invariant: a rate limit is meaningless without a positive window.
        if (RateLimit > 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RateLimitInterval.Ticks);
        }
    }
}
