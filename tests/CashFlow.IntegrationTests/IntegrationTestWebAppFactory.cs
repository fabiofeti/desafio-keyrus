using CashFlow.Modules.Consolidation.Infrastructure;
using CashFlow.Modules.Consolidation.Infrastructure.Database;
using CashFlow.Modules.Transactions.Infrastructure;
using CashFlow.Modules.Transactions.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace CashFlow.IntegrationTests;

/// <summary>
/// The API over real infrastructure: PostgreSQL 17 initialised by the same
/// <c>deploy/postgres/init</c> scripts as docker-compose (so the role isolation
/// under test is the real one), and RabbitMQ 4 for the Worker-side tests.
/// </summary>
/// <remarks>
/// Passwords here are throwaway values for containers that live as long as the
/// test run; they are not, and must not become, the compose credentials.
/// The API is pointed at the restricted roles, exactly as in docker-compose.
/// </remarks>
public sealed class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TransactionsAppRole = "transactions_app";
    public const string ConsolidationAppRole = "consolidation_app";

    private const string TransactionsAppPassword = "tx-test-only";
    private const string ConsolidationAppPassword = "co-test-only";
    private const string RabbitUser = "cashflow";
    private const string RabbitPassword = "rabbit-test-only";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cashflow")
        .WithUsername("cashflow_admin")
        .WithPassword("admin-test-only")
        .WithEnvironment("TRANSACTIONS_APP_PASSWORD", TransactionsAppPassword)
        .WithEnvironment("CONSOLIDATION_APP_PASSWORD", ConsolidationAppPassword)
        .WithResourceMapping(new DirectoryInfo(InitScriptsDirectory()), "/docker-entrypoint-initdb.d/")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername(RabbitUser)
        .WithPassword(RabbitPassword)
        .Build();

    /// <summary>Administrative connection: migrations and test setup only.</summary>
    public string AdminConnectionString => _postgres.GetConnectionString();

    public string TransactionsConnectionString => ConnectionStringFor(TransactionsAppRole, TransactionsAppPassword);

    public string ConsolidationConnectionString => ConnectionStringFor(ConsolidationAppRole, ConsolidationAppPassword);

    /// <summary>Configuration keys the Worker reads, pointed at the test broker.</summary>
    public IReadOnlyDictionary<string, string?> RabbitMqSettings => new Dictionary<string, string?>
    {
        ["RabbitMq:Host"] = _rabbitMq.Hostname,
        ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RabbitMq:Username"] = RabbitUser,
        ["RabbitMq:Password"] = RabbitPassword
    };

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        // Migrations run as the administrative user, like the compose migrator;
        // the API under test then connects with the restricted roles only.
        await MigrateAsync<TransactionsDbContext>(
            options => TransactionsModule.ConfigureDbContext(options, AdminConnectionString));
        await MigrateAsync<ConsolidationDbContext>(
            options => ConsolidationModule.ConfigureDbContext(options, AdminConnectionString));
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Transactions", TransactionsConnectionString);
        builder.UseSetting("ConnectionStrings:Consolidation", ConsolidationConnectionString);
    }

    private static async Task MigrateAsync<TContext>(Action<DbContextOptionsBuilder> configure)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        configure(builder);

        await using var context = (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
        await context.Database.MigrateAsync();
    }

    private string ConnectionStringFor(string role, string password) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Username = role, Password = password }.ConnectionString;

    private static string InitScriptsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CashFlow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("The repository root could not be located.")
            : Path.Combine(directory.FullName, "deploy", "postgres", "init");
    }
}
