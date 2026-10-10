using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Einvoice.Tests.Infrastructure;

public sealed class EinvoiceApiFactory : WebApplicationFactory<Program>
{
    public static ContainersFixture? Containers { get; set; }

    public string ClientId { get; } = "einvoice-test-client";
    public string ClientSecret { get; } = "test-secret-do-not-use-in-prod";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var c = Containers ?? throw new InvalidOperationException("ContainersFixture not initialized.");
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = c.Postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = c.Redis.GetConnectionString(),
                ["RabbitMq:Host"] = c.RabbitMq.Hostname,
                ["RabbitMq:Port"] = c.RabbitMq.GetMappedPublicPort(5672).ToString(),
                ["RabbitMq:Username"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["Oidc:ClientId"] = ClientId,
                ["Oidc:ClientSecret"] = ClientSecret,
                ["Outbox:IntervalMs"] = "500"
            });
        });
    }

    public async Task<string> GetTokenAsync(string scope = "invoice:read invoice:write")
    {
        using var client = CreateClient();
        var response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["scope"] = scope
            }));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    public HttpClient CreateAuthedClient(string scope = "invoice:read invoice:write")
    {
        var client = CreateClient();
        var token = GetTokenAsync(scope).GetAwaiter().GetResult();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
