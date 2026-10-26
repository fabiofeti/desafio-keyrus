namespace CashFlow.Common.Application.Abstractions;

/// <summary>
/// The id that ties a durable row (an outbox message) and the message published
/// from it back to the request that caused it. On the API it comes from the
/// <c>X-Correlation-Id</c> header, or is minted; on the Worker it is the
/// correlation id carried by the message being processed.
/// </summary>
public interface ICorrelationIdProvider
{
    string CorrelationId { get; }
}
