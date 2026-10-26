namespace CashFlow.Common.Application.Abstractions;

/// <summary>
/// One transaction per use case. Each module implements this over its own
/// DbContext, which is what keeps a handler from reaching into another module's
/// tables to "just save one more thing".
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
