using System.Net;
using Einvoice.Tests.Infrastructure;
using Xunit;

namespace Einvoice.Tests.Integration;

[Collection("containers")]
public class AuthTests
{
    private readonly ContainersFixture _containers;

    public AuthTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task TokenEndpoint_ReturnsAccessToken_ForValidClient()
    {
        var factory = new EinvoiceApiFactory();
        var token = await factory.GetTokenAsync();
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task HealthLive_IsPublic()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TokenEndpoint_WithWrongSecret_Returns401()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "einvoice-test-client",
                ["client_secret"] = "wrong",
                ["scope"] = "invoice:read"
            }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reseed_WithNewSecret_RepairsExistingClient()
    {
        var factoryA = new EinvoiceApiFactory();
        await factoryA.GetTokenAsync();

        const string secretB = "rotated-secret-for-reseed-test";
        var factoryB = new EinvoiceApiFactory(secretB);
        var client = factoryB.CreateClient();
        var response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = factoryB.ClientId,
                ["client_secret"] = secretB,
                ["scope"] = "invoice:read"
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
