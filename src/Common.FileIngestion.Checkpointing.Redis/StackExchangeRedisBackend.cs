using System.Diagnostics.CodeAnalysis;
using StackExchange.Redis;

namespace Common.FileIngestion.Checkpointing.Redis;

/// <summary>
/// Real <see cref="IRedisWatermarkBackend"/> over StackExchange.Redis <see cref="IDatabase"/>. A thin
/// pass-through with no logic of its own — excluded from coverage; it is exercised by integration tests
/// against a real Redis, not by unit tests.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class StackExchangeRedisBackend : IRedisWatermarkBackend
{
    private readonly IDatabase _database;

    public StackExchangeRedisBackend(IDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<string?> GetAsync(string key)
    {
        var value = await _database.StringGetAsync(key).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    public Task SetAsync(string key, string value) => _database.StringSetAsync(key, value);

    public Task DeleteAsync(string key) => _database.KeyDeleteAsync(key);
}
