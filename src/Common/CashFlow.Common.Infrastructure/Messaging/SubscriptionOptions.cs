using CashFlow.Common.Application.EventBus;

namespace CashFlow.Common.Infrastructure.Messaging;

/// <summary>
/// One subscription: which exchange and routing key feed which durable queue,
/// and the versioned message type the queue is expected to carry. Declared by
/// the consuming module; the exchange and routing key come from the producer's
/// contract.
/// </summary>
public sealed record SubscriptionOptions<TEvent>(
    string Exchange,
    string RoutingKey,
    string Queue,
    string MessageType)
    where TEvent : IIntegrationEvent
{
    /// <summary>Where the broker parks what this subscription could not process.</summary>
    public string DeadLetterQueue => $"{Queue}.dlq";
}
