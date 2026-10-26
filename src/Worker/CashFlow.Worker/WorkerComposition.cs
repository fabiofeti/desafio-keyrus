using CashFlow.Common.Application;
using CashFlow.Common.Infrastructure;
using CashFlow.Common.Infrastructure.Messaging;
using CashFlow.Modules.Transactions.Infrastructure;

namespace CashFlow.Worker;

/// <summary>
/// The Worker's composition, public so integration tests build exactly the
/// same Worker. It runs the same module assemblies as the API - handlers and
/// integration event handlers - and maps no endpoint.
/// </summary>
public static class WorkerComposition
{
    public static IServiceCollection AddWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCommonInfrastructure();
        services.AddMessaging(configuration);
        services.AddApplication(Hosting.Modules.ApplicationAssemblies);
        services.AddIntegrationEventHandlers(Hosting.Modules.PresentationAssemblies);

        // Each module registers its background side here, each with its own
        // connection string and database role - hosting both modules never
        // grants this process cross-module write access.
        services.AddTransactionsWorker(configuration);

        return services;
    }
}
