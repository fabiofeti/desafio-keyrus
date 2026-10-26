using CashFlow.Modules.Consolidation.Domain.DailyBalances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Modules.Consolidation.Infrastructure.DailyBalances;

internal sealed class DailyBalanceConfiguration : IEntityTypeConfiguration<DailyBalance>
{
    public void Configure(EntityTypeBuilder<DailyBalance> builder)
    {
        builder.ToTable("daily_balances");

        // One row per day: the ON CONFLICT target of the projection.
        builder.HasKey(b => b.BusinessDate);
        builder.Property(b => b.BusinessDate).ValueGeneratedNever();

        builder.Property(b => b.Credits).HasPrecision(18, 2);
        builder.Property(b => b.Debits).HasPrecision(18, 2);

        builder.Ignore(b => b.Balance);
    }
}
