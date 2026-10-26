using FluentValidation;

namespace CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;

/// <summary>
/// The producer validated the transaction already; this guards the module
/// against a malformed event, which is refused (and redelivered) rather than
/// summed into a balance.
/// </summary>
internal sealed class ApplyTransactionToDailyBalanceCommandValidator
    : AbstractValidator<ApplyTransactionToDailyBalanceCommand>
{
    public ApplyTransactionToDailyBalanceCommandValidator()
    {
        RuleFor(c => c.EventId).NotEmpty();
        RuleFor(c => c.BusinessDate).NotEmpty();
        RuleFor(c => c.Type)
            .Must(t => t is ApplyTransactionToDailyBalanceCommand.Credit or ApplyTransactionToDailyBalanceCommand.Debit)
            .WithMessage("Type must be 'Credit' or 'Debit'.");
        RuleFor(c => c.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
    }
}
