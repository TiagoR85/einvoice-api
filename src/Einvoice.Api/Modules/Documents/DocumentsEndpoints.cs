using Einvoice.Domain;
using Einvoice.Infrastructure.Persistence;
using Einvoice.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Einvoice.Api.Modules.Documents;

public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/invoices").RequireAuthorization();

        group.MapPost("/", Create).RequireAuthorization("invoice:write");
        group.MapGet("/", List).RequireAuthorization("invoice:read");
        group.MapGet("/{id:guid}", GetById).RequireAuthorization("invoice:read");

        return app;
    }

    private static async Task<IResult> Create(
        CreateInvoiceRequest request,
        EinvoiceDbContext db,
        RedisIdempotencyStore idempotency,
        HttpContext http,
        CancellationToken ct)
    {
        if (request is null || request.Lines is null
            || string.IsNullOrWhiteSpace(request.IssueDate) || string.IsNullOrWhiteSpace(request.DueDate))
        {
            return Results.Problem(
                title: "Validation failed",
                detail: "'lines', 'issueDate' and 'dueDate' are required.",
                statusCode: 400);
        }

        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key))
            return Results.Problem(title: "Idempotency-Key header is required.", statusCode: 400);

        var cached = await idempotency.GetAsync(key!);
        if (cached is not null)
            return Results.Text(cached, "application/json");

        try
        {
            var lines = request.Lines.Select(l => InvoiceLine.Create(l.Description, l.Quantity, l.UnitPrice, l.VatRate));
            var invoice = Invoice.Create(
                request.Number,
                DateOnly.Parse(request.IssueDate),
                DateOnly.Parse(request.DueDate),
                request.Currency,
                request.SellerId,
                request.BuyerId,
                lines);

            var payload = JsonSerializer.Serialize(new { invoiceId = invoice.Id, invoice.Number, invoice.Currency, totalAmount = invoice.TotalAmount });
            var outbox = OutboxMessage.Create("einvoice.invoice.created.v1", payload);

            db.Invoices.Add(invoice);
            db.OutboxMessages.Add(outbox);
            await db.SaveChangesAsync(ct);

            var response = invoice.ToResponse();
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await idempotency.SetAsync(key!, json, TimeSpan.FromHours(24));

            return Results.Created($"/v1/invoices/{invoice.Id}", response);
        }
        catch (DomainValidationException ex)
        {
            return Results.Problem(title: "Validation failed", detail: ex.Message, statusCode: 400);
        }
        catch (FormatException)
        {
            return Results.Problem(title: "Validation failed", detail: "Dates must be ISO 8601 (yyyy-MM-dd).", statusCode: 400);
        }
    }

    private static async Task<IResult> List(EinvoiceDbContext db, CancellationToken ct)
    {
        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .OrderByDescending(i => i.IssueDate)
            .ToListAsync(ct);
        return Results.Ok(invoices.Select(i => i.ToResponse()));
    }

    private static async Task<IResult> GetById(Guid id, EinvoiceDbContext db, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        return invoice is null ? Results.NotFound() : Results.Ok(invoice.ToResponse());
    }

    private static InvoiceResponse ToResponse(this Invoice invoice) => new(
        invoice.Id,
        invoice.Number,
        invoice.IssueDate.ToString("yyyy-MM-dd"),
        invoice.DueDate.ToString("yyyy-MM-dd"),
        invoice.Currency,
        invoice.Status.ToString(),
        invoice.SellerId,
        invoice.BuyerId,
        invoice.Lines.Select(l => new InvoiceLineResponse(l.Id, l.Description, l.Quantity, l.UnitPrice, l.VatRate, l.Total)).ToList(),
        invoice.TotalAmount);
}
