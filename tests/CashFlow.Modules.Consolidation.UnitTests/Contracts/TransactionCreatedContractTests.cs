using System.Text.Json;
using CashFlow.Common.Application.Outbox;
using CashFlow.Modules.Transactions.IntegrationEvents;

namespace CashFlow.Modules.Consolidation.UnitTests.Contracts;

/// <summary>
/// Consumer side of TransactionCreated.v1: the committed example (the producer's
/// test keeps it equal to what the outbox writes) must read into every field
/// the consolidation uses, with the deserializer the consumer uses.
/// </summary>
public class TransactionCreatedContractTests
{
    [Fact]
    public async Task CommittedExample_Should_ReadIntoTheFieldsTheConsolidationUses()
    {
        // Arrange
        string path = Path.Combine(RepositoryRoot(), "docs", "api", "events", $"{TransactionCreatedIntegrationEvent.MessageType}.json");

        // Act
        TransactionCreatedIntegrationEvent? integrationEvent = JsonSerializer.Deserialize<TransactionCreatedIntegrationEvent>(
            await File.ReadAllTextAsync(path), OutboxMessage.PayloadSerializerOptions);

        // Assert
        integrationEvent.ShouldNotBeNull();
        integrationEvent.Id.ShouldNotBe(Guid.Empty);
        integrationEvent.Type.ShouldBe("Credit");
        integrationEvent.Amount.ShouldBe(150.00m);
        integrationEvent.BusinessDate.ShouldBe(new DateOnly(2026, 10, 30));
        integrationEvent.OccurredOnUtc.ShouldBe(new DateTime(2026, 10, 30, 13, 0, 0, 125, DateTimeKind.Utc));
        integrationEvent.Version.ShouldBe(TransactionCreatedIntegrationEvent.CurrentVersion);
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
