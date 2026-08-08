using Common.Messaging.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Security.Encryption.Tests;

public sealed class EncryptionServiceCollectionExtensionsTests
{
    private const string EncryptedField = "encrypted";

    private static EncryptionPolicy Policy() => new(new Dictionary<string, ProtectionAction>
    {
        [EncryptedField] = ProtectionAction.Encrypt,
    });

    [Fact]
    public void AddEncryption_ResolvesFieldProtector_ThatRoundTrips()
    {
        var services = new ServiceCollection();
        services.AddEncryption(Policy()).AddInMemoryKeyProvider();
        using var provider = services.BuildServiceProvider();

        var protector = provider.GetRequiredService<IFieldProtector>();
        var context = new FieldProtectionContext("file-abc", 1, EncryptedField);

        var encrypted = protector.Protect(context, new ClearFieldValue("secret"));
        var recovered = protector.Unprotect(context, encrypted);

        Assert.IsType<EncryptedFieldValue>(encrypted);
        Assert.Equal(new ClearFieldValue("secret"), recovered);
    }

    [Fact]
    public void AddEncryption_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddEncryption(Policy()).AddInMemoryKeyProvider();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<AesGcmCryptoProvider>(provider.GetRequiredService<ICryptoProvider>());
        Assert.IsType<InMemoryKeyProvider>(provider.GetRequiredService<IKeyProvider>());
        Assert.Equal(
            ProtectionAction.Encrypt,
            provider.GetRequiredService<EncryptionPolicy>().GetProtection(EncryptedField));
    }

    [Fact]
    public void AddEncryption_WithoutKeyProvider_CannotResolveProtector()
    {
        var services = new ServiceCollection();
        services.AddEncryption(Policy()); // no key provider registered
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IFieldProtector>());
    }

    [Fact]
    public void AddEncryption_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddEncryption(Policy()));
    }

    [Fact]
    public void AddEncryption_NullPolicy_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddEncryption(null!));
    }

    [Fact]
    public void AddInMemoryKeyProvider_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryKeyProvider());
    }
}
