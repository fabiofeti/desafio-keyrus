using CashFlow.Common.Domain;
using CashFlow.Common.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace CashFlow.Common.Application.Decorators;

/// <summary>
/// The outermost decorator. An exception reaching here is unexpected by
/// definition - every failure the domain anticipated came back as a Result - so
/// it is logged in full and turned into a 500 without the detail leaving the
/// process.
/// </summary>
public static class ExceptionHandlingDecorator
{
    private static readonly Error Unhandled = Error.Failure(
        "Error.Unhandled",
        "An unexpected error occurred while handling the request.");

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        ILogger<CommandHandler<TCommand>> logger)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.Handle(command, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Unhandled exception in command {Command}", typeof(TCommand).Name);

                return Result.Failure(Unhandled);
            }
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.Handle(command, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Unhandled exception in command {Command}", typeof(TCommand).Name);

                return Result.Failure<TResponse>(Unhandled);
            }
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<QueryHandler<TQuery, TResponse>> logger)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.Handle(query, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Unhandled exception in query {Query}", typeof(TQuery).Name);

                return Result.Failure<TResponse>(Unhandled);
            }
        }
    }
}
