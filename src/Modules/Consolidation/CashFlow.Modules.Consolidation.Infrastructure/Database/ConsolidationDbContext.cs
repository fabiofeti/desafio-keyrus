using CashFlow.Modules.Consolidation.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Consolidation.Infrastructure.Database;

public sealed class ConsolidationDbContext(DbContextOptions<ConsolidationDbContext> options)
    : DbContext(options), IConsolidationDbContext, IConsolidationUnitOfWork
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Everything this context can see lives in one schema, and the schema
        // belongs to this module alone. A join to Transactions' tables is not
        // forbidden by convention here - it is unreachable.
        modelBuilder.HasDefaultSchema(Schemas.Consolidation);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsolidationDbContext).Assembly);
    }
}
