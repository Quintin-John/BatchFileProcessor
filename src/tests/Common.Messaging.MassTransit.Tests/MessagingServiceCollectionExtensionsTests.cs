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

        // GetRequiredService throws when a service is unregistered, so resolving without throwing is the
        // assertion — Assert.NotNull on its result can never fail and would test nothing.
        Assert.Null(Record.Exception(() => provider.GetRequiredService<IBus>()));
        // The publisher is decorated with the transport-agnostic send-retry policy.
        Assert.IsType<RetryingMessagePublisher>(provider.GetRequiredService<IMessagePublisher>());
    }

    [Fact]
    public void AddMessaging_EveryDefinedTransport_IsWired_NoValidateCapabilityGap()
    {
        // Validate() must never certify a transport the registration then rejects. This guards that
        // invariant: a new enum member added without a matching switch case fails here.
        foreach (var transport in Enum.GetValues<MessagingTransport>())
        {
            var options = new MessagingTransportOptions { Transport = transport, ConnectionString = "rabbitmq://localhost" };
            options.Validate();

            var exception = Record.Exception(() => new ServiceCollection().AddMessaging(options, Resilience()));

            Assert.Null(exception);
        }
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
