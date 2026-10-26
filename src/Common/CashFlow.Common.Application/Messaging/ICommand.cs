using CashFlow.Common.Domain;

namespace CashFlow.Common.Application.Messaging;

/// <summary>
/// Marker shared by both command shapes, so a decorator can name the command
/// type without caring whether it returns a value.
/// </summary>
public interface IBaseCommand;

/// <summary>A state change that returns nothing but success or failure.</summary>
public interface ICommand : IRequest<Result>, IBaseCommand;

/// <summary>A state change that returns a value.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;
