using Common.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common.Observability.Tests;

public sealed class SensitiveKeyRedactionServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSensitiveKeyRedaction_TypeRegistration_DecoratesLoggerFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, FakeLoggerFactory>();

        services.AddSensitiveKeyRedaction(new HashSet<string> { "Secret" });

        using var provider = services.BuildServiceProvider();
        Assert.IsType<RedactingLoggerFactory>(provider.GetRequiredService<ILoggerFactory>());
    }

    [Fact]
    public void AddSensitiveKeyRedaction_InstanceRegistration_DecoratesLoggerFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());

        services.AddSensitiveKeyRedaction(new HashSet<string>());

        using var provider = services.BuildServiceProvider();
        Assert.IsType<RedactingLoggerFactory>(provider.GetRequiredService<ILoggerFactory>());
    }

    [Fact]
    public void AddSensitiveKeyRedaction_FactoryRegistration_DecoratesLoggerFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => new FakeLoggerFactory());

        services.AddSensitiveKeyRedaction(new HashSet<string>());

        using var provider = services.BuildServiceProvider();
        Assert.IsType<RedactingLoggerFactory>(provider.GetRequiredService<ILoggerFactory>());
    }

    [Fact]
    public void AddSensitiveKeyRedaction_NoLoggingRegistered_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddSensitiveKeyRedaction(new HashSet<string>()));

    [Fact]
    public void AddSensitiveKeyRedaction_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddSensitiveKeyRedaction(new HashSet<string>()));

    [Fact]
    public void AddSensitiveKeyRedaction_NullKeys_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddSensitiveKeyRedaction(null!));

    private sealed class FakeLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
            // no-op test double
        }

        public ILogger CreateLogger(string categoryName) => throw new NotSupportedException();

        public void Dispose()
        {
            // no-op test double
        }
    }
}
