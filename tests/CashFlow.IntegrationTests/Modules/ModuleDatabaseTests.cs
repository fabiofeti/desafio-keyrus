using CashFlow.Modules.Consolidation.Infrastructure.Database;
using CashFlow.Modules.Transactions.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.IntegrationTests.Modules;

/// <summary>
/// Each module's DbContext, as the API resolves it: its own role, its own
/// schema, its own migrations history.
/// </summary>
[Trait("Category", "Integration")]
public class ModuleDatabaseTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Theory]
    [InlineData("transactions", IntegrationTestWebAppFactory.TransactionsAppRole)]
    [InlineData("consolidation", IntegrationTestWebAppFactory.ConsolidationAppRole)]
    public async Task ApiDbContext_Should_Connect_AsItsModulesRestrictedRole(string schema, string expectedRole)
    {
        // Arrange
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        DbContext context = Context(scope, schema);

        // Act
        string user = await context.Database
            .SqlQueryRaw<string>("SELECT current_user AS \"Value\"")
            .SingleAsync();

        // Assert: the API never holds the administrative credential, nor the
        // other module's role.
        user.ShouldBe(expectedRole);
    }

    [Theory]
    [InlineData("transactions")]
    [InlineData("consolidation")]
    public async Task Migrations_Should_KeepTheirHistory_InTheModulesOwnSchema(string schema)
    {
        // Arrange
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        DbContext context = Context(scope, schema);

        // Act
        string? table = await context.Database
            .SqlQuery<string?>($"SELECT to_regclass({schema + ".__ef_migrations_history"})::text AS \"Value\"")
            .SingleAsync();

        // Assert
        table.ShouldBe($"{schema}.__ef_migrations_history");
    }

    private static DbContext Context(AsyncServiceScope scope, string schema) => schema == "transactions"
        ? scope.ServiceProvider.GetRequiredService<TransactionsDbContext>()
        : scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
}
