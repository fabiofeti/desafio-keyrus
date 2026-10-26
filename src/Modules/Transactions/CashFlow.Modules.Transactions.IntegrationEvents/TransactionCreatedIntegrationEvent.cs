using CashFlow.Common.Application.EventBus;

namespace CashFlow.Modules.Transactions.IntegrationEvents;

/// <summary>
/// A transaction was recorded. Public, versioned contract: a new optional field
/// is compatible; removing, renaming or changing the meaning of a field needs
/// TransactionCreated.v2, published alongside v1 until nobody consumes v1.
/// </summary>
/// <remarks>
/// Ids and values only - no domain type and no free-text description, so no
/// personal data travels further than it needs to.
/// </remarks>
public sealed record TransactionCreatedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    Guid TransactionId,
    string Type,
    decimal Amount,
    DateOnly BusinessDate,
    DateTimeOffset OccurredAt,
    int Version,
    string CorrelationId) : IntegrationEvent(Id, OccurredOnUtc)
{
    public const string MessageType = "transactions.transaction-created.v1";
    public const string Exchange = "cashflow.transactions";
    public const string RoutingKey = "transaction-created.v1";
    public const int CurrentVersion = 1;
}
