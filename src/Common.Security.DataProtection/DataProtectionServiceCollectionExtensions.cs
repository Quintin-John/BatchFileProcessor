using Common.Security.DataProtection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the data-protection library.
/// </summary>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the crypto provider, maskers, and field protector against the given policy.
    /// A key provider is <b>not</b> registered — the caller must register one explicitly (e.g.
    /// <see cref="AddInMemoryKeyProvider"/> for dev/test, or a Key Vault provider in production),
    /// so key material is never wired in by accident.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="policy">The data-protection policy to enforce.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static IServiceCollection AddDataProtection(this IServiceCollection services, DataProtectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(policy);

        services.AddSingleton(policy);
        services.AddSingleton<ICryptoProvider, AesGcmCryptoProvider>();
        services.AddSingleton<IMasker, PanMasker>();
        services.AddSingleton<IFieldProtector, DefaultFieldProtector>();
        return services;
    }

    /// <summary>
    /// Registers the in-memory key provider. For development and testing only — production must
    /// register an HSM-backed Key Vault provider instead.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddInMemoryKeyProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IKeyProvider, InMemoryKeyProvider>();
        return services;
    }
}
