using CashFlow.Common.Application.Messaging;

namespace CashFlow.Modules.Transactions.Application.Transactions.Create;

/// <summary>
/// The idempotency key comes from a header, but the command does not know what
/// a header is: the endpoint does that translation.
/// </summary>
public sealed record CreateTransactionCommand(
    string IdempotencyKey,
    string Type,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<CreatedTransaction>;

public sealed record TransactionResponse(
    Guid Id,
    string Type,
    decimal Amount,
    DateTimeOffset OccurredAt,
    DateOnly BusinessDate,
    string? Description);

/// <summary><c>Replayed</c> tells the endpoint to mark the response; the body is the same.</summary>
public sealed record CreatedTransaction(TransactionResponse Transaction, bool Replayed);
