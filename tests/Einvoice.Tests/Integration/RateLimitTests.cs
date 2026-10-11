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
        for (var i = 0; i < 5; i++)
            statuses.Add((await client.GetAsync("/health/live")).StatusCode);

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
