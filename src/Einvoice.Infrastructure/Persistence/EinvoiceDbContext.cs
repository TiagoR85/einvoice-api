using Einvoice.Domain;
using Microsoft.EntityFrameworkCore;

namespace Einvoice.Infrastructure.Persistence;

public sealed class EinvoiceDbContext : DbContext
{
    public EinvoiceDbContext(DbContextOptions<EinvoiceDbContext> options) : base(options) { }

    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EinvoiceDbContext).Assembly);
        modelBuilder.UseOpenIddict();
    }
}
