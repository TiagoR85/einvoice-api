using Einvoice.Domain;
using Xunit;

namespace Einvoice.Tests.Unit;

public class PartyTests
{
    [Fact]
    public void Create_ValidParty_NormalizesCountryAndTrimsName()
    {
        var party = Party.Create("  Acme Lda ", "pt", "PT123456789", PartyType.Supplier);

        Assert.Equal("Acme Lda", party.Name);
        Assert.Equal("PT", party.Country);
        Assert.Equal("PT123456789", party.VatNumber);
        Assert.Equal(PartyType.Supplier, party.Type);
        Assert.NotEqual(Guid.Empty, party.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_Throws(string name)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Party.Create(name, "PT", null, PartyType.Customer));
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Create_WithInvalidCountryCode_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Party.Create("Acme", "Portugal", null, PartyType.Customer));
        Assert.Contains("Country", ex.Message);
    }

    [Fact]
    public void Create_VatNumberIsOptional()
    {
        var party = Party.Create("Acme", "PT", null, PartyType.Customer);
        Assert.Null(party.VatNumber);
    }
}
