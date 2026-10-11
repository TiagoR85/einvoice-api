using Einvoice.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Einvoice.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Type).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Payload).IsRequired();
        builder.HasIndex(o => o.PublishedAtUtc);
    }
}
