namespace CashFlow.Common.Infrastructure.Messaging;

public interface IRabbitMqPublisher
{
    /// <summary>
    /// Declares a durable topic exchange. Idempotent: safe to call on every startup.
    /// </summary>
    Task DeclareExchangeAsync(string exchange, CancellationToken cancellationToken);

    /// <summary>
    /// Returns only after the broker confirmed the message. Throws when the broker
    /// refused it, returned it as unroutable, or did not answer in time - in
    /// which case the caller must not mark it as published.
    /// </summary>
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}

public sealed record OutgoingMessage(
    string Exchange,
    string RoutingKey,
    Guid MessageId,
    string Type,
    string CorrelationId,
    ReadOnlyMemory<byte> Body);
