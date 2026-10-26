using CashFlow.Modules.Transactions.Application.Abstractions.Data;
using CashFlow.Modules.Transactions.Application.Abstractions.Time;
using CashFlow.Modules.Transactions.Infrastructure.Database;
using CashFlow.Modules.Transactions.Infrastructure.Outbox;
using CashFlow.Modules.Transactions.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Modules.Transactions.Infrastructure;

/// <summary>
/// The module's registration seam, called once by each host. Handlers,
/// validators and endpoints are not listed here - the hosts find them by
/// scanning the assemblies named in <c>Hosting.Modules</c>.
/// </summary>
public static class TransactionsModule
{
    public const string ConnectionStringName = "Transactions";

    /// <summary>API: DbContext and ports. Nothing that talks to the broker.</summary>
    public static IServiceCollection AddTransactionsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // The module's own connection string, so its own database role: hosting
        // both modules in the Worker never grants cross-module write access.
        string connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddDbContext<TransactionsDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<ITransactionsDbContext>(sp => sp.GetRequiredService<TransactionsDbContext>());
        services.AddScoped<ITransactionsUnitOfWork>(sp => sp.GetRequiredService<TransactionsDbContext>());

        services.Configure<BusinessCalendarOptions>(configuration.GetSection(BusinessCalendarOptions.SectionName));
        services.AddSingleton<IBusinessCalendar, BusinessCalendar>();

        return services;
    }

    /// <summary>Worker: the same module, plus what runs in the background.</summary>
    public static IServiceCollection AddTransactionsWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransactionsModule(configuration);

        // Needs the confirmed publisher from AddMessaging, which only the Worker registers.
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxPublisher>();

        return services;
    }

    /// <summary>Public so the migrator and the tests use exactly the same options.</summary>
    public static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Transactions))
        // snake_case, so the tables read as SQL and nobody has to quote an
        // identifier to run a query by hand.
        .UseSnakeCaseNamingConvention();
}
