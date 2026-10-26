using Npgsql;

namespace CashFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Rule 1 enforced by the database itself, not only by code review: each
/// runtime role can write its own schema's tables and cannot touch the other's.
/// The tables here are created by the administrative user, exactly as the
/// migrations will create the real ones, so the default privileges of
/// deploy/postgres/init/01-roles.sh are what is under test.
/// </summary>
[Trait("Category", "Integration")]
public class DatabaseRoleTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string InsufficientPrivilege = "42501";

    [Theory]
    [InlineData("transactions")]
    [InlineData("consolidation")]
    public async Task OwnRole_Should_ReadAndWrite_ItsOwnSchema(string schema)
    {
        // Arrange
        string table = await CreateProbeTableAsync(schema);

        // Act
        await ExecuteAsync(OwnConnection(schema), $"INSERT INTO {schema}.{table} (id) VALUES (1)");
        await ExecuteAsync(OwnConnection(schema), $"UPDATE {schema}.{table} SET id = 2");
        long count = await ScalarAsync(OwnConnection(schema), $"SELECT count(*) FROM {schema}.{table}");

        // Assert
        count.ShouldBe(1);
    }

    [Theory]
    [InlineData("transactions", "consolidation")]
    [InlineData("consolidation", "transactions")]
    public async Task Role_Should_NotWrite_TheOtherModulesSchema(string ownSchema, string otherSchema)
    {
        // Arrange
        string table = await CreateProbeTableAsync(otherSchema);

        // Act
        PostgresException exception = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(OwnConnection(ownSchema), $"INSERT INTO {otherSchema}.{table} (id) VALUES (1)"));

        // Assert
        exception.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Theory]
    [InlineData("transactions", "consolidation")]
    [InlineData("consolidation", "transactions")]
    public async Task Role_Should_NotRead_TheOtherModulesSchema(string ownSchema, string otherSchema)
    {
        // Arrange
        string table = await CreateProbeTableAsync(otherSchema);

        // Act
        PostgresException exception = await Should.ThrowAsync<PostgresException>(
            () => ScalarAsync(OwnConnection(ownSchema), $"SELECT count(*) FROM {otherSchema}.{table}"));

        // Assert
        exception.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Theory]
    [InlineData("transactions")]
    [InlineData("consolidation")]
    public async Task Role_Should_NotDelete_EvenInItsOwnSchema(string schema)
    {
        // Arrange
        string table = await CreateProbeTableAsync(schema);
        await ExecuteAsync(OwnConnection(schema), $"INSERT INTO {schema}.{table} (id) VALUES (1)");

        // Act
        PostgresException exception = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(OwnConnection(schema), $"DELETE FROM {schema}.{table}"));

        // Assert
        exception.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Theory]
    [InlineData("transactions")]
    [InlineData("consolidation")]
    public async Task Role_Should_NotCreateTables(string schema)
    {
        // Act
        PostgresException exception = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(OwnConnection(schema), $"CREATE TABLE {schema}.created_by_app (id int)"));

        // Assert: DDL belongs to the migrator, never to a runtime role.
        exception.SqlState.ShouldBe(InsufficientPrivilege);
    }

    private string OwnConnection(string schema) => schema == "transactions"
        ? Factory.TransactionsConnectionString
        : Factory.ConsolidationConnectionString;

    private async Task<string> CreateProbeTableAsync(string schema)
    {
        string table = $"probe_{Guid.NewGuid():N}";
        await ExecuteAsync(Factory.AdminConnectionString, $"CREATE TABLE {schema}.{table} (id int)");
        return table;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
