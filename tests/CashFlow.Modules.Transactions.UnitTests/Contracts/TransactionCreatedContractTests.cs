using System.Text.Json;
using System.Text.Json.Nodes;
using CashFlow.Common.Application.Outbox;
using CashFlow.Modules.Transactions.IntegrationEvents;

namespace CashFlow.Modules.Transactions.UnitTests.Contracts;

/// <summary>
/// Producer side of TransactionCreated.v1: the payload the outbox writes, for a
/// fixed event, must be the example committed in docs/api/events. A renamed or
/// retyped field fails here before it reaches a consumer. Regenerate after an
/// intended, compatible change with UPDATE_CONTRACTS=1.
/// </summary>
public class TransactionCreatedContractTests
{
    internal static readonly TransactionCreatedIntegrationEvent Example = new(
        Guid.Parse("01a17a9a-8a80-7000-8000-000000000001"),
        new DateTime(2026, 10, 30, 13, 0, 0, 125, DateTimeKind.Utc),
        Guid.Parse("01a17a9a-8943-79da-bc2f-bc477ad739e9"),
        "Credit",
        150.00m,
        new DateOnly(2026, 10, 30),
        new DateTimeOffset(2026, 10, 30, 13, 0, 0, TimeSpan.Zero),
        TransactionCreatedIntegrationEvent.CurrentVersion,
        "demo-123");

    [Fact]
    public async Task Payload_Should_MatchTheCommittedExample()
    {
        // Arrange
        string path = Path.Combine(RepositoryRoot(), "docs", "api", "events", $"{TransactionCreatedIntegrationEvent.MessageType}.json");

        // Act: the same serializer the outbox uses.
        JsonNode payload = JsonNode.Parse(JsonSerializer.Serialize(Example, OutboxMessage.PayloadSerializerOptions))!;

        // Assert
        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACTS") == "1")
        {
            await File.WriteAllTextAsync(path, payload.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }) + "\n");
            return;
        }

        JsonNode committed = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        JsonNode.DeepEquals(payload, committed).ShouldBeTrue(
            $"the TransactionCreated.v1 payload no longer matches {path}; a breaking change needs v2");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CashFlow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CashFlow.slnx not found above the test binaries.");
    }
}
