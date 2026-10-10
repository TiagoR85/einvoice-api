using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Einvoice.Infrastructure.Persistence;
using Einvoice.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Einvoice.Tests.Integration;

[Collection("containers")]
public class InvoicesTests
{
    private readonly ContainersFixture _containers;

    public InvoicesTests(ContainersFixture containers) => _containers = containers;

    private static EinvoiceApiFactory Factory() => new();

    private async Task<(Guid sellerId, Guid buyerId)> SeedPartiesAsync(HttpClient client)
    {
        var seller = await client.PostAsJsonAsync("/v1/parties",
            new { name = "Seller SA", country = "PT", vatNumber = "PT1", type = "Supplier" });
        var buyer = await client.PostAsJsonAsync("/v1/parties",
            new { name = "Buyer SA", country = "ES", vatNumber = "ES2", type = "Customer" });
        var s = JsonDocument.Parse(await seller.Content.ReadAsStringAsync()).RootElement;
        var b = JsonDocument.Parse(await buyer.Content.ReadAsStringAsync()).RootElement;
        return (s.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid());
    }

    private static object InvoicePayload(Guid sellerId, Guid buyerId) => new
    {
        number = "INV-2026-0001",
        issueDate = "2026-10-10",
        dueDate = "2026-11-09",
        currency = "EUR",
        sellerId,
        buyerId,
        lines = new[]
        {
            new { description = "Consulting", quantity = 2, unitPrice = 100, vatRate = 0.23 }
        }
    };

    [Fact]
    public async Task PostInvoice_WithWriteScope_Returns201Issued_AndWritesOutboxRow()
    {
        var client = Factory().CreateAuthedClient("invoice:write invoice:read");
        var (seller, buyer) = await SeedPartiesAsync(client);

        var response = await client.PostAsJsonAsync("/v1/invoices", InvoicePayload(seller, buyer));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Issued", body.GetProperty("status").GetString());
        var invoiceId = body.GetProperty("id").GetGuid();

        var options = new DbContextOptionsBuilder<EinvoiceDbContext>()
            .UseNpgsql(_containers.Postgres.GetConnectionString())
            .Options;
        await using var db = new EinvoiceDbContext(options);
        var outbox = await db.OutboxMessages
            .FirstOrDefaultAsync(o => o.Type == "einvoice.invoice.created.v1" && o.Payload.Contains(invoiceId.ToString()));
        Assert.NotNull(outbox);
        Assert.Null(outbox!.PublishedAtUtc);
    }

    [Fact]
    public async Task PostInvoice_WithoutToken_Returns401()
    {
        var factory = Factory();
        var authed = factory.CreateAuthedClient("invoice:write invoice:read");
        var (seller, buyer) = await SeedPartiesAsync(authed);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/invoices", InvoicePayload(seller, buyer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostInvoice_WithOnlyReadScope_Returns403()
    {
        var client = Factory().CreateAuthedClient("invoice:write invoice:read");
        var (seller, buyer) = await SeedPartiesAsync(client);

        var writeOnly = Factory().CreateAuthedClient("invoice:read");
        var response = await writeOnly.PostAsJsonAsync("/v1/invoices", InvoicePayload(seller, buyer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostInvoice_InvalidBody_Returns400()
    {
        var client = Factory().CreateAuthedClient("invoice:write invoice:read");
        var (seller, buyer) = await SeedPartiesAsync(client);
        var payload = new
        {
            number = "INV-BAD",
            issueDate = "2026-11-09",
            dueDate = "2026-10-10",
            currency = "EUR",
            sellerId = seller,
            buyerId = buyer,
            lines = new[] { new { description = "X", quantity = 1, unitPrice = 10, vatRate = 0.23 } }
        };

        var response = await client.PostAsJsonAsync("/v1/invoices", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_Returns200()
    {
        var client = Factory().CreateAuthedClient("invoice:read");
        var response = await client.GetAsync("/v1/invoices");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsCreatedInvoice()
    {
        var client = Factory().CreateAuthedClient("invoice:write invoice:read");
        var (seller, buyer) = await SeedPartiesAsync(client);
        var created = await client.PostAsJsonAsync("/v1/invoices", InvoicePayload(seller, buyer));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/v1/invoices/{id}");

        Assert.Equal("INV-2026-0001", fetched.GetProperty("number").GetString());
        Assert.Single(fetched.GetProperty("lines").EnumerateArray());
        Assert.Equal(246m, fetched.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var client = Factory().CreateAuthedClient("invoice:read");
        var response = await client.GetAsync($"/v1/invoices/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
