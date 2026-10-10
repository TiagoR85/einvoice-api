using Einvoice.Domain;
using Einvoice.Infrastructure.Persistence;
using Einvoice.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Einvoice.Tests.Integration;

[Collection("containers")]
public class PersistenceTests
{
    private readonly ContainersFixture _containers;

    public PersistenceTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task Invoice_RoundTrip_PersistsLinesAndOutbox()
    {
        var options = new DbContextOptionsBuilder<EinvoiceDbContext>()
            .UseNpgsql(_containers.Postgres.GetConnectionString())
            .Options;

        await using var seedContext = new EinvoiceDbContext(options);
        await seedContext.Database.MigrateAsync();

        var seller = Party.Create("Seller SA", "PT", "PT111", PartyType.Supplier);
        var buyer = Party.Create("Buyer SA", "ES", "ES222", PartyType.Customer);
        var line = InvoiceLine.Create("Hour", 3m, 100m, 0.23m);
        var invoice = Invoice.Create("INV-RT-1", new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 9),
            "EUR", seller.Id, buyer.Id, [line]);
        var outbox = OutboxMessage.Create("einvoice.invoice.created.v1", "{\"invoiceId\":\"" + invoice.Id + "\"}");

        seedContext.AddRange(seller, buyer, invoice, outbox);
        await seedContext.SaveChangesAsync();

        await using var readContext = new EinvoiceDbContext(options);
        var loaded = await readContext.Invoices
            .Include(i => i.Lines)
            .SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal(369m, loaded.TotalAmount);
        Assert.Equal(InvoiceStatus.Issued, loaded.Status);
        Assert.Equal(1, await readContext.OutboxMessages.CountAsync(o => o.PublishedAtUtc == null));
    }
}
