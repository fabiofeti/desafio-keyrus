using CashFlow.Common.Domain;
using CashFlow.Common.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace CashFlow.Common.Application.Decorators;

/// <summary>
/// One log entry per use case, naming the use case and whether it succeeded.
/// A failure logs the error code rather than the description, so the log stays
/// greppable and carries nothing a user typed.
/// </summary>
public static class LoggingDecorator
{
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        ILogger<CommandHandler<TCommand>> logger)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken = default)
        {
            string name = typeof(TCommand).Name;

            logger.LogInformation("Executing command {Command}", name);

            Result result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess)
            {
                logger.LogInformation("Command {Command} succeeded", name);
            }
            else
            {
                logger.LogWarning("Command {Command} failed with {ErrorCode}", name, result.Error.Code);
            }

            return result;
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
            string name = typeof(TCommand).Name;

            logger.LogInformation("Executing command {Command}", name);

            Result<TResponse> result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess)
            {
                logger.LogInformation("Command {Command} succeeded", name);
            }
            else
            {
                logger.LogWarning("Command {Command} failed with {ErrorCode}", name, result.Error.Code);
            }

            return result;
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
            string name = typeof(TQuery).Name;

            Result<TResponse> result = await inner.Handle(query, cancellationToken);

            if (result.IsFailure)
            {
                logger.LogWarning("Query {Query} failed with {ErrorCode}", name, result.Error.Code);
            }

            return result;
        }
    }
}
