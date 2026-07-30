using Common.Observability;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the observability library: OpenTelemetry tracing and metrics wired to the
/// component's instrumentation, with a config-driven resource and sampler. Exporters
/// (OTLP/Prometheus/Datadog) are a per-deployment choice the host adds on the same
/// OpenTelemetry builder — the shared library intentionally carries no exporter dependency.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>Registers observability from validated <paramref name="options"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Observability options; validated before use.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static IServiceCollection AddObservability(this IServiceCollection services, ObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(_ => new ObservabilityInstrumentation(options.ServiceName, options.ServiceVersion));

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: options.ServiceName, serviceVersion: options.ServiceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", options.Environment),
                }))
            .WithTracing(tracing => tracing
                .AddSource(options.ServiceName)
                .SetSampler(new TraceIdRatioBasedSampler(options.SamplingRatio)))
            .WithMetrics(metrics => metrics
                .AddMeter(options.ServiceName));

        return services;
    }

    /// <summary>Binds <see cref="ObservabilityOptions"/> from configuration and registers observability.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration section to bind.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ObservabilityOptions();
        configuration.Bind(options);
        return services.AddObservability(options);
    }
}
