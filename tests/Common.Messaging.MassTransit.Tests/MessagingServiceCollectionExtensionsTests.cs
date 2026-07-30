using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Common.Messaging.Contracts;

namespace Common.Messaging.MassTransit.Tests;

public sealed class MessagingServiceCollectionExtensionsTests
{
    private static MessagingTransportOptions RabbitMq() => new()
    {
        Transport = MessagingTransport.RabbitMq,
        ConnectionString = "rabbitmq://localhost",
    };

    private static MessagingResilienceOptions Resilience() => new();

    [Fact]
    public void AddMessaging_RabbitMq_ResolvesBusAndPublisher()
    {
        var services = new ServiceCollection()
            .AddMessaging(RabbitMq(), Resilience(), configure => configure.AddConsumer<BatchConsumer>());
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IBus>());
        Assert.IsType<MassTransitPublisher>(provider.GetRequiredService<IMessagePublisher>());
    }

    [Fact]
    public void AddMessaging_AzureServiceBus_IsNotYetSupported()
    {
        var options = new MessagingTransportOptions
        {
            Transport = MessagingTransport.AzureServiceBus,
            ConnectionString = "Endpoint=sb://example",
        };

        Assert.Throws<NotSupportedException>(
            () => new ServiceCollection().AddMessaging(options, Resilience()));
    }

    [Fact]
    public void AddMessaging_InvalidTransportOptions_Throws()
    {
        var options = new MessagingTransportOptions { Transport = MessagingTransport.RabbitMq, ConnectionString = "" };

        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddMessaging(options, Resilience()));
    }

    [Fact]
    public void AddMessaging_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddMessaging(RabbitMq(), Resilience()));
    }

    [Fact]
    public void AddMessaging_NullTransport_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddMessaging(null!, Resilience()));
    }

    [Fact]
    public void AddMessaging_NullResilience_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddMessaging(RabbitMq(), null!));
    }
}
