using Common.Messaging.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Security.DataProtection.Tests;

public sealed class DataProtectionServiceCollectionExtensionsTests
{
    private const string EncryptedField = "encrypted";

    private static DataProtectionPolicy Policy() => new(new Dictionary<string, ProtectionAction>
    {
        [EncryptedField] = ProtectionAction.Encrypt,
    });

    [Fact]
    public void AddDataProtection_ResolvesFieldProtector_ThatRoundTrips()
    {
        var services = new ServiceCollection();
        services.AddDataProtection(Policy()).AddInMemoryKeyProvider();
        using var provider = services.BuildServiceProvider();

        var protector = provider.GetRequiredService<IFieldProtector>();
        var context = new FieldProtectionContext("file-abc", 1, EncryptedField);

        var encrypted = protector.Protect(context, new ClearFieldValue("secret"));
        var recovered = protector.Unprotect(context, encrypted);

        Assert.IsType<EncryptedFieldValue>(encrypted);
        Assert.Equal(new ClearFieldValue("secret"), recovered);
    }

    [Fact]
    public void AddDataProtection_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddDataProtection(Policy()).AddInMemoryKeyProvider();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<AesGcmCryptoProvider>(provider.GetRequiredService<ICryptoProvider>());
        Assert.IsType<InMemoryKeyProvider>(provider.GetRequiredService<IKeyProvider>());
        Assert.Equal(
            ProtectionAction.Encrypt,
            provider.GetRequiredService<DataProtectionPolicy>().GetProtection(EncryptedField));
    }

    [Fact]
    public void AddDataProtection_WithoutKeyProvider_CannotResolveProtector()
    {
        var services = new ServiceCollection();
        services.AddDataProtection(Policy()); // no key provider registered
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IFieldProtector>());
    }

    [Fact]
    public void AddDataProtection_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddDataProtection(Policy()));
    }

    [Fact]
    public void AddDataProtection_NullPolicy_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddDataProtection(null!));
    }

    [Fact]
    public void AddInMemoryKeyProvider_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryKeyProvider());
    }
}
