using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Einvoice.Tests.Infrastructure;
using Xunit;

namespace Einvoice.Tests.Integration;

[Collection("containers")]
public class PartiesTests
{
    private readonly ContainersFixture _containers;

    public PartiesTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task PostParty_WithWriteScope_Returns201()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateAuthedClient("invoice:write");
        var response = await client.PostAsJsonAsync("/v1/parties",
            new { name = "Acme Lda", country = "PT", vatNumber = "PT123", type = "Supplier" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PartyResponseDto>();
        Assert.NotNull(body);
        Assert.Equal("PT", body!.Country);
    }

    [Fact]
    public async Task PostParty_WithOnlyReadScope_Returns403()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateAuthedClient("invoice:read");
        var response = await client.PostAsJsonAsync("/v1/parties",
            new { name = "Acme", country = "PT", vatNumber = (string?)null, type = "Customer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostParty_WithoutToken_Returns401()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/parties",
            new { name = "Acme", country = "PT", vatNumber = (string?)null, type = "Customer" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostParty_InvalidCountry_Returns400()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateAuthedClient("invoice:write");
        var response = await client.PostAsJsonAsync("/v1/parties",
            new { name = "Acme", country = "Portugal", vatNumber = (string?)null, type = "Customer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_Returns200()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateAuthedClient("invoice:read");
        var list = await client.GetFromJsonAsync<List<PartyResponseDto>>("/v1/parties");
        Assert.NotNull(list);
    }

    [Fact]
    public async Task GetAll_WithoutToken_Returns401()
    {
        var factory = new EinvoiceApiFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/parties");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AccessToken_ContainsSubAndScopeClaims()
    {
        var factory = new EinvoiceApiFactory();
        var token = await factory.GetTokenAsync("invoice:read");

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = (payload.Length % 4) switch
        {
            2 => payload + "==",
            3 => payload + "=",
            _ => payload
        };

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        Assert.True(doc.RootElement.TryGetProperty("sub", out var sub));
        Assert.Equal("einvoice-test-client", sub.GetString());
        Assert.True(doc.RootElement.TryGetProperty("scope", out var scope));
        Assert.Contains("invoice:read", scope.GetString());
    }

    public record PartyResponseDto(Guid Id, string Name, string Country, string? VatNumber, string Type);
}
