namespace Einvoice.Api.Modules.Documents;

public record CreateInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, decimal VatRate);
public record CreateInvoiceRequest(
    string Number, string IssueDate, string DueDate, string Currency,
    Guid SellerId, Guid BuyerId, List<CreateInvoiceLineRequest> Lines);
public record InvoiceLineResponse(Guid Id, string Description, decimal Quantity, decimal UnitPrice, decimal VatRate, decimal Total);
public record InvoiceResponse(
    Guid Id, string Number, string IssueDate, string DueDate, string Currency,
    string Status, Guid SellerId, Guid BuyerId, List<InvoiceLineResponse> Lines, decimal TotalAmount);
