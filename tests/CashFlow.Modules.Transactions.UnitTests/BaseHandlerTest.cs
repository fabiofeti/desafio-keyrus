using CashFlow.Modules.Transactions.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Transactions.UnitTests;

/// <summary>
/// The module's real DbContext over in-memory Sqlite, kept open for the test's
/// lifetime. What depends on PostgreSQL itself (unique-violation translation,
/// grants, jsonb) is covered by the integration tests instead.
/// </summary>
public abstract class BaseHandlerTest : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected BaseHandlerTest()
    {
        _connection.Open();
    }

    protected TransactionsDbContext CreateDbContext()
    {
        DbContextOptions<TransactionsDbContext> options = new DbContextOptionsBuilder<TransactionsDbContext>()
            .UseSqlite(_connection)
            // Same column names as production, so the outbox index filter is valid SQL.
            .UseSnakeCaseNamingConvention()
            .Options;

        var context = new TransactionsDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
