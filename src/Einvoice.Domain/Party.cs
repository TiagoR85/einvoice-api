namespace Einvoice.Domain;

public enum PartyType
{
    Customer = 0,
    Supplier = 1
}

public sealed class Party
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Country { get; private set; } = default!;
    public string? VatNumber { get; private set; }
    public PartyType Type { get; private set; }

    private Party() { } // EF

    public static Party Create(string name, string country, string? vatNumber, PartyType type)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Name is required.");
        if (string.IsNullOrWhiteSpace(country) || country.Trim().Length != 2)
            throw new DomainValidationException("Country must be a 2-letter ISO 3166-1 alpha-2 code.");

        return new Party
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Country = country.Trim().ToUpperInvariant(),
            VatNumber = string.IsNullOrWhiteSpace(vatNumber) ? null : vatNumber.Trim(),
            Type = type
        };
    }
}
