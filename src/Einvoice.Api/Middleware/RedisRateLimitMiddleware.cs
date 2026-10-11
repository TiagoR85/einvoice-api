using StackExchange.Redis;

namespace Einvoice.Api.Middleware;

public sealed class RedisRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;
    private readonly int _perMinute;

    public RedisRateLimitMiddleware(RequestDelegate next, IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _next = next;
        _redis = redis;
        _perMinute = int.TryParse(configuration["RateLimit:PerMinute"], out var v) ? v : 120;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rl:{partition}:{window}";

        var db = _redis.GetDatabase();
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(2));

        if (count > _perMinute)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsync("Rate limit exceeded.");
            return;
        }

        await _next(context);
    }
}
