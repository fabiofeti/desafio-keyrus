using CashFlow.Modules.Transactions.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Transactions.Infrastructure.Database;

public sealed class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options)
    : DbContext(options), ITransactionsDbContext, ITransactionsUnitOfWork
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Everything this context can see lives in one schema, and the schema
        // belongs to this module alone. A join to Consolidation's tables is not
        // forbidden by convention here - it is unreachable.
        modelBuilder.HasDefaultSchema(Schemas.Transactions);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransactionsDbContext).Assembly);

        // OutboxMessageConfiguration (Common.Infrastructure) is applied here
        // together with the first aggregate and its migration.
    }
}
