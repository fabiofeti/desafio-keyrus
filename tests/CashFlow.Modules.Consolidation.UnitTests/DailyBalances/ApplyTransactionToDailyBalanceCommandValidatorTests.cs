using CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;
using FluentValidation.TestHelper;

namespace CashFlow.Modules.Consolidation.UnitTests.DailyBalances;

public class ApplyTransactionToDailyBalanceCommandValidatorTests
{
    private readonly ApplyTransactionToDailyBalanceCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Pass_WhenCommandIsValid()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_Should_Fail_WhenEventIdIsEmpty()
    {
        _validator.TestValidate(Valid() with { EventId = Guid.Empty }).ShouldHaveValidationErrorFor(c => c.EventId);
    }

    [Fact]
    public void Validate_Should_Fail_WhenBusinessDateIsMissing()
    {
        _validator.TestValidate(Valid() with { BusinessDate = default }).ShouldHaveValidationErrorFor(c => c.BusinessDate);
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("Transfer")]
    [InlineData("")]
    public void Validate_Should_Fail_WhenTypeIsUnknown(string type)
    {
        _validator.TestValidate(Valid() with { Type = type }).ShouldHaveValidationErrorFor(c => c.Type);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    public void Validate_Should_Fail_WhenAmountIsInvalid(string amount)
    {
        _validator.TestValidate(Valid() with { Amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) })
            .ShouldHaveValidationErrorFor(c => c.Amount);
    }

    private static ApplyTransactionToDailyBalanceCommand Valid() =>
        new(Guid.NewGuid(), new DateOnly(2026, 10, 26), "Debit", 10.50m, new DateTime(2026, 10, 26, 13, 0, 0, DateTimeKind.Utc));
}
