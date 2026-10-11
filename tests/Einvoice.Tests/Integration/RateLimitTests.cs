using System.Net;
using Einvoice.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Einvoice.Tests.Integration;

public class LowRateLimitFactory : EinvoiceApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:PerMinute"] = "3"
            });
        });
    }
}

public class ZeroRateLimitFactory : EinvoiceApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:PerMinute"] = "0"
            });
        });
    }
}

[Collection("containers")]
public class RateLimitTests
{
    public RateLimitTests(ContainersFixture containers) => _ = containers;

    [Fact]
    public async Task RequestsAboveLimit_Return429()
    {
        var factory = new LowRateLimitFactory();
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !statuses.Contains(HttpStatusCode.TooManyRequests))
        {
            statuses.Add((await client.GetAsync("/v1/parties")).StatusCode);
            await Task.Delay(50);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.All(statuses, s => Assert.True(
            s is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests,
            $"Unexpected status {(int)s}"));
    }

    [Fact]
    public async Task HealthEndpoints_SkipRateLimit_EvenWhenLimitIsZero()
    {
        var factory = new ZeroRateLimitFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/v1/parties")).StatusCode);
    }
}
