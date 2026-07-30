namespace Common.Messaging.MassTransit.Tests;

public sealed class MessagingOptionsTests
{
    [Fact]
    public void Transport_Validate_Valid_DoesNotThrow()
    {
        new MessagingTransportOptions
        {
            Transport = MessagingTransport.RabbitMq,
            ConnectionString = "amqps://host",
        }.Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Transport_Validate_BlankConnectionString_Throws(string? connectionString)
    {
        var options = new MessagingTransportOptions { ConnectionString = connectionString! };

        Assert.ThrowsAny<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Transport_Validate_UndefinedTransport_Throws()
    {
        var options = new MessagingTransportOptions
        {
            Transport = (MessagingTransport)99,
            ConnectionString = "x",
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Transport_Properties_RoundTrip()
    {
        var options = new MessagingTransportOptions
        {
            Transport = MessagingTransport.AzureServiceBus,
            ConnectionString = "sb://x",
            EndpointPrefix = "g266",
        };

        Assert.Equal(MessagingTransport.AzureServiceBus, options.Transport);
        Assert.Equal("sb://x", options.ConnectionString);
        Assert.Equal("g266", options.EndpointPrefix);
    }

    [Fact]
    public void Resilience_Validate_Defaults_DoNotThrow()
    {
        new MessagingResilienceOptions().Validate();
    }

    [Fact]
    public void Resilience_Properties_RoundTrip()
    {
        var options = new MessagingResilienceOptions
        {
            RetryLimit = 3,
            RetryInitialInterval = TimeSpan.FromSeconds(2),
            RetryIntervalIncrement = TimeSpan.FromSeconds(3),
            UseJitter = false,
            CircuitBreakerTripThreshold = 20,
            CircuitBreakerActiveThreshold = 5,
            CircuitBreakerResetInterval = TimeSpan.FromMinutes(2),
            RateLimit = 100,
        };

        Assert.Equal(3, options.RetryLimit);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryInitialInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), options.RetryIntervalIncrement);
        Assert.False(options.UseJitter);
        Assert.Equal(20, options.CircuitBreakerTripThreshold);
        Assert.Equal(5, options.CircuitBreakerActiveThreshold);
        Assert.Equal(TimeSpan.FromMinutes(2), options.CircuitBreakerResetInterval);
        Assert.Equal(100, options.RateLimit);
    }

    [Fact]
    public void Resilience_Validate_NegativeRetryLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessagingResilienceOptions { RetryLimit = -1 }.Validate());
    }

    [Fact]
    public void Resilience_Validate_NegativeInterval_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessagingResilienceOptions { RetryInitialInterval = TimeSpan.FromSeconds(-1) }.Validate());
    }

    [Fact]
    public void Resilience_Validate_TripThresholdAbove100_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessagingResilienceOptions { CircuitBreakerTripThreshold = 101 }.Validate());
    }

    [Fact]
    public void Resilience_Validate_NegativeRateLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessagingResilienceOptions { RateLimit = -1 }.Validate());
    }
}
