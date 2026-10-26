using CashFlow.Common.Infrastructure.Messaging;
using CashFlow.Modules.Consolidation.Application.Abstractions.Data;
using CashFlow.Modules.Consolidation.Application.Abstractions.Projections;
using CashFlow.Modules.Consolidation.Infrastructure.DailyBalances;
using CashFlow.Modules.Consolidation.Infrastructure.Database;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Modules.Consolidation.Infrastructure;

/// <summary>
/// The module's registration seam, called once by each host. Handlers,
/// validators and endpoints are not listed here - the hosts find them by
/// scanning the assemblies named in <c>Hosting.Modules</c>.
/// </summary>
public static class ConsolidationModule
{
    public const string ConnectionStringName = "Consolidation";

    /// <summary>This module's durable queue for the v1 event; versioned like the contract.</summary>
    public const string TransactionCreatedQueue = "consolidation.transaction-created.v1";

    /// <summary>API: DbContext and ports, for the daily balance reads. Nothing that talks to the broker.</summary>
    public static IServiceCollection AddConsolidationModule(this IServiceCollection services, IConfiguration configuration)
    {
        // The module's own connection string, so its own database role: hosting
        // both modules in the Worker never grants cross-module write access.
        string connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddDbContext<ConsolidationDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<IConsolidationDbContext>(sp => sp.GetRequiredService<ConsolidationDbContext>());
        services.AddScoped<IConsolidationUnitOfWork>(sp => sp.GetRequiredService<ConsolidationDbContext>());
        services.AddScoped<IDailyBalanceProjection, DailyBalanceProjection>();

        return services;
    }

    /// <summary>Worker: the same module, plus what runs in the background.</summary>
    public static IServiceCollection AddConsolidationWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddConsolidationModule(configuration);

        services.AddIntegrationEventConsumer(new SubscriptionOptions<TransactionCreatedIntegrationEvent>(
            TransactionCreatedIntegrationEvent.Exchange,
            TransactionCreatedIntegrationEvent.RoutingKey,
            TransactionCreatedQueue,
            TransactionCreatedIntegrationEvent.MessageType));

        return services;
    }

    /// <summary>Public so the migrator and the tests use exactly the same options.</summary>
    public static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Consolidation))
        // snake_case, so the tables read as SQL and nobody has to quote an
        // identifier to run a query by hand.
        .UseSnakeCaseNamingConvention();
}
