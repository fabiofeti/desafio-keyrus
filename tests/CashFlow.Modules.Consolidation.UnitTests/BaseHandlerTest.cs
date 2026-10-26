using CashFlow.Modules.Consolidation.Infrastructure;
using CashFlow.Modules.Consolidation.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Consolidation.UnitTests;

/// <summary>
/// The module's real DbContext over in-memory Sqlite, kept open for the test's
/// lifetime. The projection's SQL (ON CONFLICT, the inbox CTE) is PostgreSQL's
/// and is covered by the integration tests instead.
/// </summary>
public abstract class BaseHandlerTest : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected BaseHandlerTest()
    {
        _connection.Open();
    }

    protected ConsolidationDbContext CreateDbContext()
    {
        DbContextOptions<ConsolidationDbContext> options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        var context = new ConsolidationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>DailyBalance has no public writer - production writes it with SQL, so do the tests.</summary>
    protected static Task SeedBalanceAsync(
        ConsolidationDbContext context, DateOnly date, decimal credits, decimal debits, DateTime updatedAtUtc) =>
        context.Database.ExecuteSqlAsync(
            $"INSERT INTO daily_balances (business_date, credits, debits, updated_at_utc) VALUES ({date}, {credits}, {debits}, {updatedAtUtc})");

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
