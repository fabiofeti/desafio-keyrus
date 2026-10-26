using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Modules.Consolidation.Infrastructure.Inbox;

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");

        // The deduplication key: the producer's event id.
        builder.HasKey(m => m.EventId);
        builder.Property(m => m.EventId).ValueGeneratedNever();

        builder.Property(m => m.MessageType).HasMaxLength(200);
    }
}
