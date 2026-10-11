namespace Einvoice.Domain;

public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4
}

public sealed class Invoice
{
    public Guid Id { get; private set; }
    public string Number { get; private set; } = default!;
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string Currency { get; private set; } = default!;
    public InvoiceStatus Status { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid BuyerId { get; private set; }

    private readonly List<InvoiceLine> _lines = new();
    public IReadOnlyCollection<InvoiceLine> Lines => _lines;
    public decimal TotalAmount => _lines.Sum(l => l.Total);

    private Invoice() { } // EF

    public static Invoice Create(
        string number,
        DateOnly issueDate,
        DateOnly dueDate,
        string currency,
        Guid sellerId,
        Guid buyerId,
        IEnumerable<InvoiceLine> lines)
    {
        if (string.IsNullOrWhiteSpace(number))
            throw new DomainValidationException("Number is required.");
        if (dueDate < issueDate)
            throw new DomainValidationException("DueDate must not be before IssueDate.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new DomainValidationException("Currency must be a 3-letter ISO 4217 code.");
        if (sellerId == Guid.Empty)
            throw new DomainValidationException("SellerId is required.");
        if (buyerId == Guid.Empty)
            throw new DomainValidationException("BuyerId is required.");
        if (sellerId == buyerId)
            throw new DomainValidationException("Seller and buyer must be different parties.");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            Number = number.Trim(),
            IssueDate = issueDate,
            DueDate = dueDate,
            Currency = currency.Trim().ToUpperInvariant(),
            Status = InvoiceStatus.Issued,
            SellerId = sellerId,
            BuyerId = buyerId
        };

        foreach (var line in lines)
            invoice._lines.Add(line);

        if (invoice._lines.Count == 0)
            throw new DomainValidationException("At least one line is required.");

        return invoice;
    }

    public void MarkProcessing()
    {
        if (Status != InvoiceStatus.Issued)
            throw new DomainValidationException($"Cannot move invoice from {Status} to Processing.");
        Status = InvoiceStatus.Processing;
    }

    public void MarkCompleted()
    {
        if (Status != InvoiceStatus.Processing)
            throw new DomainValidationException($"Cannot move invoice from {Status} to Completed.");
        Status = InvoiceStatus.Completed;
    }

    public void MarkFailed()
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.Processing))
            throw new DomainValidationException($"Cannot move invoice from {Status} to Failed.");
        Status = InvoiceStatus.Failed;
    }
}
