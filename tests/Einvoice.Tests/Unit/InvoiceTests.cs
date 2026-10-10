using Einvoice.Domain;
using Xunit;

namespace Einvoice.Tests.Unit;

public class InvoiceLineTests
{
    [Fact]
    public void Total_MultipliesQuantityPriceAndVat()
    {
        var line = InvoiceLine.Create("Consulting hour", 2m, 100m, 0.23m);

        Assert.Equal(246m, line.Total);
    }

    [Fact]
    public void Create_WithZeroQuantity_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            InvoiceLine.Create("Bad", 0m, 100m, 0.23m));

        Assert.Contains("Quantity", ex.Message);
    }

    [Fact]
    public void Create_WithNegativeUnitPrice_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            InvoiceLine.Create("Bad", 1m, -1m, 0.23m));

        Assert.Contains("UnitPrice", ex.Message);
    }

    [Fact]
    public void Create_WithVatRateOutOfRange_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            InvoiceLine.Create("Bad", 1m, 100m, 1.5m));

        Assert.Contains("VatRate", ex.Message);
    }
}

public class InvoiceTests
{
    private static IEnumerable<InvoiceLine> OneLine() =>
        [InvoiceLine.Create("Consulting", 1m, 100m, 0.23m)];

    [Fact]
    public void Create_ValidInvoice_SetsIssuedAndComputesTotal()
    {
        var invoice = Invoice.Create("INV-001", new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 9),
            "EUR", Guid.NewGuid(), Guid.NewGuid(), OneLine());

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(123m, invoice.TotalAmount);
        Assert.Single(invoice.Lines);
    }

    [Fact]
    public void Create_WithDueDateBeforeIssueDate_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Invoice.Create("INV-001", new DateOnly(2026, 11, 9), new DateOnly(2026, 10, 10),
                "EUR", Guid.NewGuid(), Guid.NewGuid(), OneLine()));

        Assert.Contains("DueDate", ex.Message);
    }

    [Fact]
    public void Create_WithNoLines_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Invoice.Create("INV-001", new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 9),
                "EUR", Guid.NewGuid(), Guid.NewGuid(), []));

        Assert.Contains("line", ex.Message);
    }

    [Fact]
    public void Create_WithSameSellerAndBuyer_Throws()
    {
        var party = Guid.NewGuid();
        var ex = Assert.Throws<DomainValidationException>(() =>
            Invoice.Create("INV-001", new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 9),
                "EUR", party, party, OneLine()));

        Assert.Contains("different parties", ex.Message);
    }

    [Fact]
    public void StatusTransitions_IssuedProcessingCompleted()
    {
        var invoice = Invoice.Create("INV-001", new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 9),
            "EUR", Guid.NewGuid(), Guid.NewGuid(), OneLine());

        invoice.MarkProcessing();
        invoice.MarkCompleted();

        Assert.Equal(InvoiceStatus.Completed, invoice.Status);
    }

    [Fact]
    public void MarkCompleted_FromIssued_Throws()
    {
        var invoice = Invoice.Create("INV-001", new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 9),
            "EUR", Guid.NewGuid(), Guid.NewGuid(), OneLine());

        var ex = Assert.Throws<DomainValidationException>(() => invoice.MarkCompleted());
        Assert.Contains("Issued", ex.Message);
    }
}
