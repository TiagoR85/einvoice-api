using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Einvoice.Tests.Infrastructure;
using RabbitMQ.Client;
using Xunit;

namespace Einvoice.Tests.Integration;

[Collection("containers")]
public class InvoiceCompletedConsumerTests
{
    private readonly ContainersFixture _containers;

    public InvoiceCompletedConsumerTests(ContainersFixture containers) => _containers = containers;

    private static string CompletedEvent(Guid invoiceId) => $$$"""
        {"specversion":"1.0","id":"{{{Guid.NewGuid()}}}","type":"einvoice.invoice.completed.v1",
         "source":"urn:einvoice-worker","data":{"invoiceId":"{{{invoiceId}}}",
         "objectKey":"invoices/{{{invoiceId}}}.pdf","contentType":"application/pdf",
         "sizeBytes":123,"completedAt":"2026-10-12T00:00:00Z"}}
        """;

    private async Task<IConnection> ConnectAsync()
    {
        return await new ConnectionFactory
        {
            HostName = _containers.RabbitMq.Hostname,
            Port = _containers.RabbitMq.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq"
        }.CreateConnectionAsync();
    }

    private static async Task PublishAsync(IConnection connection, string body)
    {
        await using var channel = await connection.CreateChannelAsync();
        var props = new BasicProperties { ContentType = "application/cloudevents+json" };
        await channel.BasicPublishAsync("einvoice.events", "einvoice.invoice.completed.v1",
            mandatory: false, basicProperties: props, body: Encoding.UTF8.GetBytes(body));
    }

    private async Task<HttpClient> AuthedClientAsync(EinvoiceApiFactory factory)
    {
        var token = await factory.GetTokenAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<InvoiceDto> CreateInvoiceAsync(HttpClient http)
    {
        var parties = await http.GetFromJsonAsync<List<PartyDto>>("/v1/parties");
        if (parties is null || parties.Count < 2)
        {
            var sellerRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/parties")
            {
                Content = JsonContent.Create(new { name = "Inbox Seller", country = "PT", vatNumber = "PT1", type = "Supplier" })
            };
            var seller = await http.SendAsync(sellerRequest);
            var buyerRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/parties")
            {
                Content = JsonContent.Create(new { name = "Inbox Buyer", country = "ES", vatNumber = (string?)null, type = "Customer" })
            };
            var buyer = await http.SendAsync(buyerRequest);
            parties =
            [
                (await seller.Content.ReadFromJsonAsync<PartyDto>())!,
                (await buyer.Content.ReadFromJsonAsync<PartyDto>())!
            ];
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/invoices")
        {
            Content = JsonContent.Create(new
            {
                number = $"INBOX-{Guid.NewGuid():N}"[..16],
                issueDate = "2026-10-12",
                dueDate = "2026-11-12",
                currency = "EUR",
                sellerId = parties[0].Id,
                buyerId = parties[1].Id,
                lines = new[] { new { description = "Inbox test", quantity = 1, unitPrice = 10, vatRate = 0.23 } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var created = await http.SendAsync(request);
        Assert.True(created.IsSuccessStatusCode);
        var invoice = await created.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.NotNull(invoice);
        Assert.Equal("Issued", invoice!.Status);
        return invoice;
    }

    [Fact]
    public async Task Completed_event_flips_invoice_status_to_completed()
    {
        var factory = new EinvoiceApiFactory();
        using var http = await AuthedClientAsync(factory);
        var invoice = await CreateInvoiceAsync(http);

        await using var connection = await ConnectAsync();
        await PublishAsync(connection, CompletedEvent(invoice.Id));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        var status = "Issued";
        while (DateTime.UtcNow < deadline && status != "Completed")
        {
            var current = await http.GetFromJsonAsync<InvoiceDto>($"/v1/invoices/{invoice.Id}");
            status = current?.Status ?? "Issued";
            if (status != "Completed")
                await Task.Delay(200);
        }

        Assert.Equal("Completed", status);
    }

    [Fact]
    public async Task Duplicate_completed_event_is_idempotent()
    {
        var factory = new EinvoiceApiFactory();
        using var http = await AuthedClientAsync(factory);
        var invoice = await CreateInvoiceAsync(http);

        await using var connection = await ConnectAsync();
        await PublishAsync(connection, CompletedEvent(invoice.Id));
        await PublishAsync(connection, CompletedEvent(invoice.Id));

        await Task.Delay(3000);

        var current = await http.GetFromJsonAsync<InvoiceDto>($"/v1/invoices/{invoice.Id}");
        Assert.Equal("Completed", current!.Status);
    }

    private sealed record PartyDto(Guid Id, string Name, string Country, string? VatNumber, string Type);
    private sealed record InvoiceDto(Guid Id, string Number, string Status);
}
