using CashFlow.Modules.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Modules.Transactions.Infrastructure.Transactions;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Description).HasMaxLength(200);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(100);
        builder.Property(t => t.RequestFingerprint).HasMaxLength(64);

        // The database is the last word on idempotency: two concurrent requests
        // with the same key both pass the handler's lookup, only one insert wins.
        builder.HasIndex(t => t.IdempotencyKey).IsUnique();

        // GET /transactions?date=
        builder.HasIndex(t => t.BusinessDate);
    }
}
