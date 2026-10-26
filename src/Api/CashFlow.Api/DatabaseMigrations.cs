using CashFlow.Modules.Consolidation.Infrastructure.Database;
using CashFlow.Modules.Transactions.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Api;

/// <summary>
/// Brings every module's schema up to date, one line per module that owns a
/// database. Runs only in the --migrate bootstrap mode, with administrative
/// credentials; the schemas hold no foreign key into each other, so the order
/// does not matter.
/// </summary>
internal static class DatabaseMigrations
{
    public static async Task ApplyAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseMigrations));

        await scope.ServiceProvider.GetRequiredService<TransactionsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>().Database.MigrateAsync();

        logger.LogInformation("Database migrations applied");
    }
}
