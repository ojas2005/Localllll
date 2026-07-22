using System.Text.Json;
using StackExchange.Redis;

namespace Localll.Common.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);
    Task RemoveAsync(string key);
    Task<string?> GetStringAsync(string key);
    Task SetStringAsync(string key, string value, TimeSpan? ttl = null);
}

public class RedisCacheService(IConnectionMultiplexer redis) : ICacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);
    private IDatabase Db => redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await Db.StringGetAsync(key);
        return value.HasValue ? JsonSerializer.Deserialize<T>(value!) : default;
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) =>
        Db.StringSetAsync(key, JsonSerializer.Serialize(value), ttl ?? DefaultTtl);

    public Task RemoveAsync(string key) => Db.KeyDeleteAsync(key);

    public async Task<string?> GetStringAsync(string key)
    {
        var value = await Db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public Task SetStringAsync(string key, string value, TimeSpan? ttl = null) =>
        Db.StringSetAsync(key, value, ttl ?? DefaultTtl);
}
