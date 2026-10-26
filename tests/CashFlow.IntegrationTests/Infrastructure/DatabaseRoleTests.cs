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

    [Theory]
    [InlineData("consolidation", "SELECT count(*) FROM transactions.transactions")]
    [InlineData("consolidation", "SELECT count(*) FROM transactions.outbox_messages")]
    [InlineData("transactions", "SELECT count(*) FROM consolidation.daily_balances")]
    [InlineData("transactions", "SELECT count(*) FROM consolidation.inbox_messages")]
    public async Task Role_Should_NotRead_TheOtherModulesRealTables(string ownSchema, string sql)
    {
        // Act: the tables the migrations created, not probes.
        PostgresException exception = await Should.ThrowAsync<PostgresException>(
            () => ScalarAsync(OwnConnection(ownSchema), sql));

        // Assert
        exception.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Theory]
    [InlineData("transactions", "INSERT INTO consolidation.daily_balances (business_date, credits, debits, updated_at_utc) VALUES ('2099-01-01', 1, 0, now())")]
    [InlineData("transactions", "UPDATE consolidation.daily_balances SET credits = credits + 1")]
    [InlineData("transactions", "INSERT INTO consolidation.inbox_messages (event_id, message_type, processed_at_utc) VALUES (gen_random_uuid(), 'x', now())")]
    [InlineData("consolidation", "INSERT INTO transactions.transactions (id, type, amount, occurred_at, business_date, idempotency_key, request_fingerprint, created_at_utc) VALUES (gen_random_uuid(), 'Credit', 1, now(), '2099-01-01', 'role-test', 'x', now())")]
    [InlineData("consolidation", "INSERT INTO transactions.outbox_messages (id, type, payload, correlation_id, occurred_on_utc, attempt_count) VALUES (gen_random_uuid(), 'x', '{}', 'x', now(), 0)")]
    public async Task Role_Should_NotWrite_TheOtherModulesRealTables(string ownSchema, string sql)
    {
        // Act: rule 1 on the tables the migrations created, not probes.
        PostgresException exception = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(OwnConnection(ownSchema), sql));

        // Assert
        exception.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Fact]
    public async Task ConsolidationRole_Should_NotWrite_TheTransactionsOutbox()
    {
        // Act
        PostgresException exception = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(Factory.ConsolidationConnectionString,
                "UPDATE transactions.outbox_messages SET published_on_utc = now()"));

        // Assert
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
