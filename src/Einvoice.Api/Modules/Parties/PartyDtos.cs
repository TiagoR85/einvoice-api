using Einvoice.Domain;

namespace Einvoice.Api.Modules.Parties;

public record CreatePartyRequest(string Name, string Country, string? VatNumber, string Type);
public record PartyResponse(Guid Id, string Name, string Country, string? VatNumber, string Type);

public static class PartyMapping
{
    public static PartyResponse ToResponse(this Party party) =>
        new(party.Id, party.Name, party.Country, party.VatNumber, party.Type.ToString());

    public static PartyType ToPartyType(this string type) => type switch
    {
        "Customer" => PartyType.Customer,
        "Supplier" => PartyType.Supplier,
        _ => throw new DomainValidationException("Type must be 'Customer' or 'Supplier'.")
    };
}
