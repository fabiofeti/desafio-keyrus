using CashFlow.Modules.Transactions.Application.Abstractions.Data;
using CashFlow.Modules.Transactions.Infrastructure;
using CashFlow.Modules.Transactions.Infrastructure.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Modules.Transactions.UnitTests;

public class TransactionsModuleTests
{
    [Fact]
    public void AddTransactionsModule_Should_Throw_WhenItsConnectionStringIsMissing()
    {
        // Arrange: a shared connection string is not a fallback - each module
        // must be given its own, and so its own database role.
        IConfiguration configuration = Configuration(("ConnectionStrings:Database", "Host=db"));

        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddTransactionsModule(configuration));

        // Assert
        exception.Message.ShouldContain("'Transactions'");
    }

    [Fact]
    public void AddTransactionsModule_Should_ResolveDbContextAndUnitOfWork_AsTheSameScopedInstance()
    {
        // Arrange
        IConfiguration configuration = Configuration(("ConnectionStrings:Transactions", "Host=db;Username=transactions_app"));
        using ServiceProvider provider = new ServiceCollection().AddTransactionsModule(configuration).BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        // Act
        TransactionsDbContext context = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();

        // Assert: one context per scope, so the handler's writes and its
        // SaveChangesAsync are one local transaction.
        scope.ServiceProvider.GetRequiredService<ITransactionsDbContext>().ShouldBeSameAs(context);
        scope.ServiceProvider.GetRequiredService<ITransactionsUnitOfWork>().ShouldBeSameAs(context);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
}
