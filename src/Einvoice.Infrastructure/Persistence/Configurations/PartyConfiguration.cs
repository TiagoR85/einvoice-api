using Einvoice.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Einvoice.Infrastructure.Persistence.Configurations;

public sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Country).HasMaxLength(2).IsRequired();
        builder.Property(p => p.VatNumber).HasMaxLength(32);
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(16);
    }
}
