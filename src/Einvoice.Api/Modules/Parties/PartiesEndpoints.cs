using Einvoice.Domain;
using Einvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Einvoice.Api.Modules.Parties;

public static class PartiesEndpoints
{
    public static IEndpointRouteBuilder MapParties(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/parties").RequireAuthorization();

        group.MapPost("/", Create).RequireAuthorization("invoice:write");
        group.MapGet("/", List).RequireAuthorization("invoice:read");
        group.MapGet("/{id:guid}", GetById).RequireAuthorization("invoice:read");

        return app;
    }

    private static async Task<IResult> Create(CreatePartyRequest request, EinvoiceDbContext db, CancellationToken ct)
    {
        try
        {
            var party = Party.Create(request.Name, request.Country, request.VatNumber, request.Type.ToPartyType());
            db.Parties.Add(party);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/parties/{party.Id}", party.ToResponse());
        }
        catch (DomainValidationException ex)
        {
            return Results.Problem(title: "Validation failed", detail: ex.Message, statusCode: 400);
        }
    }

    private static async Task<IResult> List(EinvoiceDbContext db, CancellationToken ct) =>
        Results.Ok((await db.Parties.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct))
            .Select(p => p.ToResponse()));

    private static async Task<IResult> GetById(Guid id, EinvoiceDbContext db, CancellationToken ct)
    {
        var party = await db.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return party is null ? Results.NotFound() : Results.Ok(party.ToResponse());
    }
}
