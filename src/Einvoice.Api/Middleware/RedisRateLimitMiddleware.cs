using StackExchange.Redis;

namespace Einvoice.Api.Middleware;

public sealed class RedisRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;
    private readonly int _perMinute;
    private readonly ILogger<RedisRateLimitMiddleware> _logger;

    public RedisRateLimitMiddleware(
        RequestDelegate next,
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisRateLimitMiddleware> logger)
    {
        _next = next;
        _redis = redis;
        _perMinute = int.TryParse(configuration["RateLimit:PerMinute"], out var v) ? v : 120;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rl:{partition}:{window}";

        try
        {
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
        }
        catch (Exception ex)
        {
            // Fail-open: a Redis outage must never turn into a 500 for the caller.
            _logger.LogWarning(ex, "Rate limit check failed for {Path}; allowing the request.", path);
        }

        await _next(context);
    }
}
