using Common.Messaging.MassTransit;
using Microsoft.Extensions.Configuration;

namespace Ingestion.Worker.Tests;

/// <summary>
/// Guards that the now-immutable (init-only) messaging options still bind from configuration the way the
/// composition root does — Get&lt;T&gt; constructs and populates them — and that a malformed value fails
/// closed rather than silently falling back to a default.
/// </summary>
public sealed class MessagingBindingTests
{
    [Fact]
    public void ResilienceOptions_BindFromConfiguration_KeepingDefaultsForAbsentKeys()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:RetryLimit"] = "9",
                ["Resilience:CircuitBreakerTripThreshold"] = "42",
            })
            .Build();

        var options = config.GetSection("Resilience").Get<MessagingResilienceOptions>();

        Assert.NotNull(options);
        Assert.Equal(9, options!.RetryLimit);
        Assert.Equal(42, options.CircuitBreakerTripThreshold);
        Assert.Equal(10, options.CircuitBreakerActiveThreshold); // absent key retains its default
    }

    [Fact]
    public void ResilienceOptions_MalformedValue_FailsBinding_NotSilentDefault()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Resilience:RetryLimit"] = "notanumber" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => config.GetSection("Resilience").Get<MessagingResilienceOptions>());
    }
}
