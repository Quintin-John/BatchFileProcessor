using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Common.Observability.Tests;

public sealed class ObservabilityServiceCollectionExtensionsTests
{
    [Fact]
    public void AddObservability_RegistersInstrumentationAndProviders()
    {
        var services = new ServiceCollection();
        services.AddObservability(new ObservabilityOptions { ServiceName = "svc", ServiceVersion = "1.0.0" });
        using var provider = services.BuildServiceProvider();

        // GetRequiredService throws when a service is unregistered, so resolving without throwing is the
        // assertion — Assert.NotNull on its result can never fail and would test nothing.
        Assert.Equal("svc", provider.GetRequiredService<ObservabilityInstrumentation>().Name);
        Assert.Null(Record.Exception(() => provider.GetRequiredService<TracerProvider>()));
        Assert.Null(Record.Exception(() => provider.GetRequiredService<MeterProvider>()));
    }

    [Fact]
    public void AddObservability_FromConfiguration_BindsOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceName"] = "svc2",
                ["Environment"] = "prod",
                ["SamplingRatio"] = "0.5",
            })
            .Build();

        var services = new ServiceCollection().AddObservability(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<ObservabilityOptions>();
        Assert.Equal("svc2", options.ServiceName);
        Assert.Equal("prod", options.Environment);
        Assert.Equal(0.5, options.SamplingRatio);
    }

    [Fact]
    public void AddObservability_InvalidOptions_Throws()
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(
            () => services.AddObservability(new ObservabilityOptions { ServiceName = "" }));
    }

    [Fact]
    public void AddObservability_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddObservability(new ObservabilityOptions { ServiceName = "svc" }));
    }

    [Fact]
    public void AddObservability_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddObservability((ObservabilityOptions)null!));
    }

    [Fact]
    public void AddObservability_NullConfiguration_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddObservability((IConfiguration)null!));
    }
}
