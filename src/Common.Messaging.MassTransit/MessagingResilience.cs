using MassTransit;

namespace Common.Messaging.MassTransit;

/// <summary>
/// Applies the resilience conventions — retry with incremental backoff, circuit breaker, and
/// optional rate limiting — to a receive endpoint, so every consumer behaves consistently.
/// </summary>
public static class MessagingResilience
{
    /// <summary>Applies resilience to a receive endpoint from validated options.</summary>
    /// <param name="endpoint">The receive endpoint to configure.</param>
    /// <param name="options">Resilience options; validated before use.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static void Apply(IReceiveEndpointConfigurator endpoint, MessagingResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (options.RateLimit > 0)
        {
            endpoint.UseRateLimit(options.RateLimit, options.RateLimitInterval);
        }

        endpoint.UseMessageRetry(retry =>
            retry.Incremental(options.RetryLimit, options.RetryInitialInterval, options.RetryIntervalIncrement));

        endpoint.UseCircuitBreaker(breaker =>
        {
            breaker.TripThreshold = options.CircuitBreakerTripThreshold;
            breaker.ActiveThreshold = options.CircuitBreakerActiveThreshold;
            breaker.ResetInterval = options.CircuitBreakerResetInterval;
        });
    }
}
