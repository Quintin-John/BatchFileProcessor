using MassTransit;
using Common.Messaging.Contracts;
using Common.Messaging.MassTransit;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the messaging library: configures MassTransit for the chosen transport
/// with the contract serializer and resilience conventions, and registers
/// <see cref="IMessagePublisher"/>. Topology is provisioned by infrastructure — the app declares
/// no exchanges/queues of its own.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>Registers MassTransit and the publisher from validated options.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="transport">Transport options; validated.</param>
    /// <param name="resilience">Resilience options; validated and applied to every endpoint.</param>
    /// <param name="configure">Optional hook to register consumers and other MassTransit components.</param>
    /// <exception cref="ArgumentNullException">Any required argument is null.</exception>
    /// <exception cref="NotSupportedException">The selected transport is not yet configured.</exception>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        MessagingTransportOptions transport,
        MessagingResilienceOptions resilience,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(resilience);
        transport.Validate();
        resilience.Validate();

        services.AddSingleton(transport);
        services.AddSingleton(resilience);

        services.AddMassTransit(bus =>
        {
            configure?.Invoke(bus);

            // Consistent endpoint naming across services (kebab-case, optional configured prefix).
            bus.SetEndpointNameFormatter(
                new KebabCaseEndpointNameFormatter(transport.EndpointPrefix ?? string.Empty, includeNamespace: false));

            bus.AddConfigureEndpointsCallback((_, _, endpoint) => MessagingResilience.Apply(endpoint, resilience));

            switch (transport.Transport)
            {
                case MessagingTransport.RabbitMq:
                    bus.UsingRabbitMq((context, rabbit) =>
                    {
                        rabbit.Host(new Uri(transport.ConnectionString));
                        rabbit.ConfigureJsonSerializerOptions(options =>
                        {
                            MessagingSerialization.Configure(options);
                            return options;
                        });
                        rabbit.ConfigureEndpoints(context);
                    });
                    break;

                default:
                    throw new NotSupportedException($"Transport '{transport.Transport}' is not yet configured.");
            }
        });

        services.AddSingleton<IMessagePublisher, MassTransitPublisher>();
        return services;
    }
}
