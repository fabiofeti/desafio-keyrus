namespace CashFlow.Common.Application.Abstractions;

/// <summary>
/// The scoped holder behind <see cref="ICorrelationIdProvider"/>. Whatever
/// opens the scope sets it once - the HTTP middleware from the
/// <c>X-Correlation-Id</c> header, the broker consumer from the message - and
/// everything inside the scope reads the same id. Unset, it mints one.
/// </summary>
public sealed class CorrelationContext : ICorrelationIdProvider
{
    private string? _correlationId;

    public string CorrelationId => _correlationId ??= Guid.NewGuid().ToString("N");

    public void Set(string correlationId) => _correlationId = correlationId;
}
