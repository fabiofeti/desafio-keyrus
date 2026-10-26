using CashFlow.Common.Application.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Common.Infrastructure.Outbox;

/// <summary>
/// Applied explicitly by the DbContext of a module that publishes, so the table
/// lands in that module's own schema. Never shared between modules.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        // The id is the integration event's id, minted by the producer.
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Type).HasMaxLength(200);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.CorrelationId).HasMaxLength(100);
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // The publisher only ever reads unpublished rows, oldest first.
        builder.HasIndex(m => m.OccurredOnUtc)
            .HasFilter("published_on_utc IS NULL")
            .HasDatabaseName("ix_outbox_messages_unpublished");
    }
}
