using Common.Observability;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration that hooks sensitive-value redaction into the logging pipeline.
/// </summary>
public static class SensitiveKeyRedactionServiceCollectionExtensions
{
    /// <summary>
    /// Decorates the registered <see cref="ILoggerFactory"/> so every logger redacts structured values whose
    /// key is in <paramref name="sensitiveKeys"/>, before they reach any sink. Logging must already be
    /// registered (e.g. via <c>AddLogging</c>). Registering it once, at composition, covers all loggers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sensitiveKeys">Field keys whose values must never appear in clear in the logs; may be empty.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">No <see cref="ILoggerFactory"/> is registered yet.</exception>
    public static IServiceCollection AddSensitiveKeyRedaction(this IServiceCollection services, IReadOnlySet<string> sensitiveKeys)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sensitiveKeys);

        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ILoggerFactory))
            ?? throw new InvalidOperationException(
                "AddSensitiveKeyRedaction requires logging to be registered first (call AddLogging).");

        services.Remove(existing);
        services.Add(ServiceDescriptor.Describe(
            typeof(ILoggerFactory),
            provider => new RedactingLoggerFactory(BuildInner(existing, provider), sensitiveKeys),
            existing.Lifetime));

        return services;
    }

    // Materialises the decorated factory from whichever registration form the original descriptor used.
    private static ILoggerFactory BuildInner(ServiceDescriptor descriptor, IServiceProvider provider) =>
        (ILoggerFactory)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
}
