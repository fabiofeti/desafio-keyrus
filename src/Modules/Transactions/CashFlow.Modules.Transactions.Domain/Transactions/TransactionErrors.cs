using CashFlow.Common.Domain;

namespace CashFlow.Modules.Transactions.Domain.Transactions;

public static class TransactionErrors
{
    public static readonly Error UnknownType = Error.Problem(
        "Transactions.UnknownType", "The type must be Credit or Debit.");

    public static readonly Error AmountMustBePositive = Error.Problem(
        "Transactions.AmountMustBePositive", "The amount must be greater than zero.");

    public static readonly Error AmountPrecision = Error.Problem(
        "Transactions.AmountPrecision", "The amount must have at most two decimal places.");

    public static Error IdempotencyKeyReused(string key) => Error.Conflict(
        "Transactions.IdempotencyKeyReused",
        $"The Idempotency-Key '{key}' was already used with a different payload.");
}
