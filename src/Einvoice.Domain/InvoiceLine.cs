namespace Einvoice.Domain;

public sealed class InvoiceLine
{
    public Guid Id { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal Total => Quantity * UnitPrice * (1 + VatRate);

    private InvoiceLine() { } // EF

    public static InvoiceLine Create(string description, decimal quantity, decimal unitPrice, decimal vatRate)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainValidationException("Description is required.");
        if (quantity <= 0)
            throw new DomainValidationException("Quantity must be greater than zero.");
        if (unitPrice < 0)
            throw new DomainValidationException("UnitPrice must not be negative.");
        if (vatRate is < 0 or > 1)
            throw new DomainValidationException("VatRate must be between 0 and 1.");

        return new InvoiceLine
        {
            Id = Guid.NewGuid(),
            Description = description.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice,
            VatRate = vatRate
        };
    }
}
