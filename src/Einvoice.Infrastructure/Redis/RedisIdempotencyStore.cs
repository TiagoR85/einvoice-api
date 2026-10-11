using Microsoft.Extensions.Caching.Distributed;
using System.Text;

namespace Einvoice.Infrastructure.Redis;

public sealed class RedisIdempotencyStore
{
    private readonly IDistributedCache _cache;

    public RedisIdempotencyStore(IDistributedCache cache) => _cache = cache;

    public async Task<string?> GetAsync(string key)
    {
        var bytes = await _cache.GetAsync($"idem:{key}");
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task SetAsync(string key, string value, TimeSpan ttl)
    {
        await _cache.SetAsync($"idem:{key}", Encoding.UTF8.GetBytes(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    }
}
