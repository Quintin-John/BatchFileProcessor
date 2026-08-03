using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Checkpointing.Redis;

namespace Common.FileIngestion.Tests.Checkpointing;

public sealed class RedisCheckpointStoreTests
{
    private const string Prefix = "wm:";

    private static Watermark Sample(string sourceKey = "src.dat") => new(sourceKey, "HASH", 1200, 100, 5);

    private static (RedisCheckpointStore Store, FakeBackend Backend) Build()
    {
        var backend = new FakeBackend();
        return (new RedisCheckpointStore(backend, Prefix), backend);
    }

    [Fact]
    public async Task Save_ThenLoad_RoundTrips_UnderPrefixedKey()
    {
        var (store, backend) = Build();
        var watermark = Sample();

        await store.SaveAsync(watermark, CancellationToken.None);

        Assert.True(backend.Store.ContainsKey("wm:src.dat")); // prefix + source key
        var loaded = await store.LoadAsync("src.dat", CancellationToken.None);
        Assert.Equal(watermark, loaded);
    }

    [Fact]
    public async Task Load_Missing_ReturnsNull()
    {
        var (store, _) = Build();

        Assert.Null(await store.LoadAsync("absent.dat", CancellationToken.None));
    }

    [Fact]
    public async Task Clear_RemovesTheWatermark()
    {
        var (store, _) = Build();
        await store.SaveAsync(Sample(), CancellationToken.None);

        await store.ClearAsync("src.dat", CancellationToken.None);

        Assert.Null(await store.LoadAsync("src.dat", CancellationToken.None));
    }

    [Fact]
    public async Task Save_NullWatermark_Throws()
    {
        var (store, _) = Build();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Load_BlankSourceKey_Throws()
    {
        var (store, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync("  ", CancellationToken.None));
    }

    [Fact]
    public async Task Load_CancelledToken_ThrowsBeforeTouchingBackend()
    {
        var (store, backend) = Build();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.LoadAsync("src.dat", cts.Token));
        Assert.Equal(0, backend.Calls); // failed fast; no Redis command issued
    }

    [Fact]
    public async Task Save_CancelledToken_ThrowsBeforeTouchingBackend()
    {
        var (store, backend) = Build();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(Sample(), cts.Token));
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task Clear_CancelledToken_ThrowsBeforeTouchingBackend()
    {
        var (store, backend) = Build();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.ClearAsync("src.dat", cts.Token));
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public void Constructor_NullBackend_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RedisCheckpointStore((IRedisWatermarkBackend)null!, Prefix));

    [Fact]
    public void Constructor_BlankPrefix_Throws() =>
        Assert.Throws<ArgumentException>(() => new RedisCheckpointStore(new FakeBackend(), "  "));

    private sealed class FakeBackend : IRedisWatermarkBackend
    {
        public Dictionary<string, string> Store { get; } = new(StringComparer.Ordinal);

        /// <summary>Number of backend operations invoked — proves a cancelled call never reaches Redis.</summary>
        public int Calls { get; private set; }

        public Task<string?> GetAsync(string key)
        {
            Calls++;
            return Task.FromResult(Store.TryGetValue(key, out var value) ? value : null);
        }

        public Task SetAsync(string key, string value)
        {
            Calls++;
            Store[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key)
        {
            Calls++;
            Store.Remove(key);
            return Task.CompletedTask;
        }
    }
}
