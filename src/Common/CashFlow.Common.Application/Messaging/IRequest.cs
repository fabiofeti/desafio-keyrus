namespace CashFlow.Common.Application.Messaging;

/// <summary>
/// Ties a message to the type its handler returns. Nothing resolves this
/// directly - it exists so <see cref="ICommand"/> and <see cref="IQuery{T}"/>
/// share one definition of "what comes back".
/// </summary>
public interface IRequest<TResponse>;
