using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Application.Outbox;
using CashFlow.Common.Infrastructure.Outbox;
using CashFlow.Modules.Transactions.Application.Abstractions.Data;
using CashFlow.Modules.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CashFlow.Modules.Transactions.Infrastructure.Database;

public sealed class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options)
    : DbContext(options), ITransactionsDbContext, ITransactionsUnitOfWork
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// A lost race on a unique index becomes a provider-free exception the
    /// Application can handle - the idempotency key is the case that matters.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            throw new UniqueConstraintViolationException(postgres.ConstraintName, exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Everything this context can see lives in one schema, and the schema
        // belongs to this module alone. A join to Consolidation's tables is not
        // forbidden by convention here - it is unreachable.
        modelBuilder.HasDefaultSchema(Schemas.Transactions);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransactionsDbContext).Assembly);

        // Shared row shape, applied explicitly: it lives in Common.Infrastructure,
        // outside the assembly scan above, and lands in this module's schema.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
