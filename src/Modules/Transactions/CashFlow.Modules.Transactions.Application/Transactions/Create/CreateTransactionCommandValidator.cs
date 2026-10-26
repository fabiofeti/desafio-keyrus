using CashFlow.Modules.Transactions.Domain.Transactions;
using FluentValidation;

namespace CashFlow.Modules.Transactions.Application.Transactions.Create;

/// <summary>
/// Shape only. "This key was already used" is a business rule and lives in the
/// handler, next to the data.
/// </summary>
internal sealed class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    public const decimal MaxAmount = 999_999_999.99m;

    public CreateTransactionCommandValidator()
    {
        RuleFor(c => c.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[A-Za-z0-9_-]+$");

        // Names only: Enum.TryParse would also accept "1" or "99".
        RuleFor(c => c.Type)
            .Must(t => Enum.GetNames<TransactionType>().Contains(t))
            .WithMessage("Type must be 'Credit' or 'Debit'.");

        RuleFor(c => c.Amount)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaxAmount)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);

        RuleFor(c => c.OccurredAt).NotEmpty();

        RuleFor(c => c.Description).MaximumLength(200);
    }
}
