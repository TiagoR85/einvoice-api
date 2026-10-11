using StackExchange.Redis;

namespace Einvoice.Infrastructure.Redis;

public sealed class RedisIdempotencyStore
{
    public const string PendingMarker = "__pending__";

    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis) => _redis = redis;

    private static string Key(string key) => $"idem:{key}";

    public async Task<string?> GetAsync(string key)
    {
        var value = await _redis.GetDatabase().StringGetAsync(Key(key));
        return value.IsNull ? null : value.ToString();
    }

    public async Task<bool> TryClaimAsync(string key, TimeSpan ttl) =>
        await _redis.GetDatabase().StringSetAsync(Key(key), PendingMarker, ttl, When.NotExists);

    public async Task SetAsync(string key, string value, TimeSpan ttl) =>
        await _redis.GetDatabase().StringSetAsync(Key(key), value, ttl);

    public async Task ReleaseAsync(string key) =>
        await _redis.GetDatabase().KeyDeleteAsync(Key(key));
}
