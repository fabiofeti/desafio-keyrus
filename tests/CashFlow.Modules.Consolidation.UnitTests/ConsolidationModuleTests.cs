using CashFlow.Modules.Consolidation.Application.Abstractions.Data;
using CashFlow.Modules.Consolidation.Infrastructure;
using CashFlow.Modules.Consolidation.Infrastructure.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Modules.Consolidation.UnitTests;

public class ConsolidationModuleTests
{
    [Fact]
    public void AddConsolidationModule_Should_Throw_WhenItsConnectionStringIsMissing()
    {
        // Arrange: a shared connection string is not a fallback - each module
        // must be given its own, and so its own database role.
        IConfiguration configuration = Configuration(("ConnectionStrings:Database", "Host=db"));

        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddConsolidationModule(configuration));

        // Assert
        exception.Message.ShouldContain("'Consolidation'");
    }

    [Fact]
    public void AddConsolidationModule_Should_ResolveDbContextAndUnitOfWork_AsTheSameScopedInstance()
    {
        // Arrange
        IConfiguration configuration = Configuration(("ConnectionStrings:Consolidation", "Host=db;Username=consolidation_app"));
        using ServiceProvider provider = new ServiceCollection().AddConsolidationModule(configuration).BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        // Act
        ConsolidationDbContext context = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();

        // Assert: one context per scope, so the handler's writes and its
        // SaveChangesAsync are one local transaction.
        scope.ServiceProvider.GetRequiredService<IConsolidationDbContext>().ShouldBeSameAs(context);
        scope.ServiceProvider.GetRequiredService<IConsolidationUnitOfWork>().ShouldBeSameAs(context);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
}
