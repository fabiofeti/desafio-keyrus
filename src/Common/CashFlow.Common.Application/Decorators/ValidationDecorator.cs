using CashFlow.Common.Domain;
using CashFlow.Common.Application.Messaging;
using FluentValidation;
using FluentValidation.Results;

namespace CashFlow.Common.Application.Decorators;

/// <summary>
/// Input shape, checked before the handler runs. Business rules
/// that need the database belong in the handler; this only answers "is this
/// request well formed".
/// </summary>
public static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken = default)
        {
            ValidationFailure[] failures = await Validate(command, validators, cancellationToken);

            return failures.Length == 0
                ? await inner.Handle(command, cancellationToken)
                : Result.Failure(ToValidationError(failures));
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken = default)
        {
            ValidationFailure[] failures = await Validate(command, validators, cancellationToken);

            return failures.Length == 0
                ? await inner.Handle(command, cancellationToken)
                : Result.Failure<TResponse>(ToValidationError(failures));
        }
    }

    private static async Task<ValidationFailure[]> Validate<TCommand>(
        TCommand command,
        IEnumerable<IValidator<TCommand>> validators,
        CancellationToken cancellationToken)
    {
        IValidator<TCommand>[] active = [.. validators];

        if (active.Length == 0)
        {
            return [];
        }

        var context = new ValidationContext<TCommand>(command);

        ValidationResult[] results = await Task.WhenAll(
            active.Select(v => v.ValidateAsync(context, cancellationToken)));

        return [.. results.SelectMany(r => r.Errors).Where(f => f is not null)];
    }

    private static ValidationError ToValidationError(ValidationFailure[] failures) =>
        new([.. failures.Select(f => Error.Problem(f.ErrorCode, f.ErrorMessage))]);
}
