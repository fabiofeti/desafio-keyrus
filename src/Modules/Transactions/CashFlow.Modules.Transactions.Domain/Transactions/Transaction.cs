using CashFlow.Common.Domain;

namespace CashFlow.Modules.Transactions.Domain.Transactions;

/// <summary>
/// A cash-flow entry. Immutable once created: there is no update and no delete
/// (the runtime database role has no DELETE either); a correction is another
/// transaction.
/// </summary>
public sealed class Transaction : Entity
{
    private Transaction()
    {
    }

    public Guid Id { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>Always positive; the sign comes from <see cref="Type"/>.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// When it happened, normalised to UTC - PostgreSQL's timestamptz keeps the
    /// instant, not the offset. The local day is already in <see cref="BusinessDate"/>.
    /// </summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// The day this entry counts for, computed once from <see cref="OccurredAt"/>
    /// in the business time zone and stored - never recomputed on read.
    /// </summary>
    public DateOnly BusinessDate { get; private set; }

    public string? Description { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the normalised request: tells "the same request, retried"
    /// apart from "the same key, a different payload".
    /// </summary>
    public string RequestFingerprint { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public static Result<Transaction> Create(
        TransactionType type,
        decimal amount,
        DateTimeOffset occurredAt,
        DateOnly businessDate,
        string? description,
        string idempotencyKey,
        string requestFingerprint,
        DateTime nowUtc)
    {
        // Domain invariants, independent of HTTP: the validator checks the
        // request's shape, the aggregate guarantees no path creates an invalid entry.
        if (!Enum.IsDefined(type))
        {
            return Result.Failure<Transaction>(TransactionErrors.UnknownType);
        }

        if (amount <= 0)
        {
            return Result.Failure<Transaction>(TransactionErrors.AmountMustBePositive);
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return Result.Failure<Transaction>(TransactionErrors.AmountPrecision);
        }

        return new Transaction
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Amount = amount,
            OccurredAt = occurredAt.ToUniversalTime(),
            BusinessDate = businessDate,
            Description = description,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            CreatedAtUtc = nowUtc
        };
    }
}
