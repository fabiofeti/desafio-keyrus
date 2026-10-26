namespace CashFlow.Common.Application.Abstractions;

/// <summary>
/// A write lost a race on a unique index. Raised by a module's unit of work so
/// Application code can react to it without knowing which database provider
/// produced it - the classic case is two requests with the same idempotency key.
/// </summary>
public sealed class UniqueConstraintViolationException(string? constraintName, Exception innerException)
    : Exception($"A unique constraint was violated: {constraintName ?? "unknown"}", innerException)
{
    public string? ConstraintName { get; } = constraintName;
}
