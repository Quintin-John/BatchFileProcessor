using System.Text.Json;
using Common.FileIngestion.Abstractions;
using StackExchange.Redis;

namespace Common.FileIngestion.Checkpointing.Redis;

/// <summary>
/// Redis-backed <see cref="ICheckpointStore"/> for cross-instance resume: watermarks are stored as JSON
/// string values keyed by a configured prefix plus the source key, so a brand-new instance (not just the
/// same pod/volume) can resume a crashed job. Redis <c>SET</c> is atomic; at-rest durability is Redis
/// persistence (AOF/RDB), an infra concern. Watermark monotonicity is enforced by the pipeline, so this
/// store is a plain persist/load/clear.
/// </summary>
public sealed class RedisCheckpointStore : ICheckpointStore
{
    private readonly IRedisWatermarkBackend _backend;
    private readonly string _keyPrefix;

    /// <summary>Creates a store over a Redis connection.</summary>
    /// <param name="redis">Connected Redis multiplexer; required.</param>
    /// <param name="keyPrefix">Prefix applied to every watermark key (e.g. <c>ingestion:watermark:</c>); required, non-blank.</param>
    /// <exception cref="ArgumentNullException"><paramref name="redis"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="keyPrefix"/> is blank.</exception>
    public RedisCheckpointStore(IConnectionMultiplexer redis, string keyPrefix)
        : this(WrapDatabase(redis), keyPrefix)
    {
    }

    internal RedisCheckpointStore(IRedisWatermarkBackend backend, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        _backend = backend;
        _keyPrefix = keyPrefix;
    }

    /// <inheritdoc />
    public async Task<Watermark?> LoadAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var json = await _backend.GetAsync(KeyFor(sourceKey)).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<Watermark>(json);
    }

    /// <inheritdoc />
    public Task SaveAsync(Watermark watermark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watermark);
        return _backend.SetAsync(KeyFor(watermark.SourceKey), JsonSerializer.Serialize(watermark));
    }

    /// <inheritdoc />
    public Task ClearAsync(string sourceKey, CancellationToken cancellationToken) =>
        _backend.DeleteAsync(KeyFor(sourceKey));

    private string KeyFor(string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        return _keyPrefix + sourceKey;
    }

    private static StackExchangeRedisBackend WrapDatabase(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        return new StackExchangeRedisBackend(redis.GetDatabase());
    }
}
