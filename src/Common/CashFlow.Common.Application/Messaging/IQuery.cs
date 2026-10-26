using CashFlow.Common.Domain;

namespace CashFlow.Common.Application.Messaging;

/// <summary>A read. Never changes state, always projects to a response type.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
