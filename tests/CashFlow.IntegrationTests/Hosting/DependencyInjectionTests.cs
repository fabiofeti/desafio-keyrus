using CashFlow.Common.Application.EventBus;
using CashFlow.Common.Application.Messaging;
using CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;
using CashFlow.Modules.Consolidation.Application.DailyBalances.Get;
using CashFlow.Modules.Transactions.Application.Abstractions.Pagination;
using CashFlow.Modules.Transactions.Application.Transactions.Create;
using CashFlow.Modules.Transactions.Application.Transactions.List;
using CashFlow.Modules.Transactions.IntegrationEvents;
using CashFlow.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CashFlow.IntegrationTests.Hosting;

/// <summary>
/// The whole container of each host, built the way Production builds it but
/// with ValidateOnBuild and ValidateScopes on: a missing registration or a
/// singleton holding a scoped service fails here, not on the first request.
/// (Development turns both on by default; Production does not.)
/// </summary>
[Trait("Category", "Integration")]
public class DependencyInjectionTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public void Api_Should_BuildAndResolveEveryHandler_InProduction()
    {
        // Arrange
        using WebApplicationFactory<Program> production = Factory.WithWebHostBuilder(builder => builder
            .UseEnvironment(Environments.Production)
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            }));

        // Act: building the host validates every descriptor.
        using IServiceScope scope = production.Services.CreateScope();

        // Assert: the decorated handlers the endpoints ask for.
        scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateTransactionCommand, CreatedTransaction>>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IQueryHandler<ListTransactionsQuery, PagedResponse<TransactionListItem>>>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IQueryHandler<GetDailyBalanceQuery, DailyBalanceResponse>>().ShouldNotBeNull();
    }

    [Fact]
    public void Worker_Should_BuildAndResolveEveryHandler_InProduction()
    {
        // Arrange
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(Factory.RabbitMqSettings)
        {
            ["ConnectionStrings:Transactions"] = Factory.TransactionsConnectionString,
            ["ConnectionStrings:Consolidation"] = Factory.ConsolidationConnectionString
        });
        builder.Services.AddWorker(builder.Configuration);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        }));

        // Act: not started - nothing connects to the broker.
        using IHost host = builder.Build();
        using IServiceScope scope = host.Services.CreateScope();

        // Assert: the consumer's handler chain and every background service.
        scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TransactionCreatedIntegrationEvent>>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyTransactionToDailyBalanceCommand>>().ShouldNotBeNull();
        host.Services.GetServices<IHostedService>().Count().ShouldBeGreaterThanOrEqualTo(2);
    }
}
