using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Einvoice.Infrastructure.Persistence;

public sealed class EinvoiceDbContextFactory : IDesignTimeDbContextFactory<EinvoiceDbContext>
{
    public EinvoiceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<EinvoiceDbContext>()
            .UseNpgsql("Host=localhost;Database=einvoice_design;Username=design;Password=design")
            .Options);
}
