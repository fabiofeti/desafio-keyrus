using CashFlow.Modules.Transactions.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.IntegrationTests.Modules;

[Trait("Category", "Integration")]
public class TransactionsModuleTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ApiDbContext_Should_Connect_AsTheRestrictedRole()
    {
        // Arrange
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        TransactionsDbContext context = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();

        // Act
        string user = await context.Database
            .SqlQueryRaw<string>("SELECT current_user AS \"Value\"")
            .SingleAsync();

        // Assert: the API never holds the administrative credential.
        user.ShouldBe(IntegrationTestWebAppFactory.TransactionsAppRole);
    }

    [Fact]
    public async Task Migrations_Should_KeepTheirHistory_InTheModulesOwnSchema()
    {
        // Arrange
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        TransactionsDbContext context = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();

        // Act: the restricted role resolves to_regclass only inside its own schema.
        string? table = await context.Database
            .SqlQueryRaw<string?>("SELECT to_regclass('transactions.__ef_migrations_history')::text AS \"Value\"")
            .SingleAsync();

        // Assert
        table.ShouldBe("transactions.__ef_migrations_history");
    }
}
