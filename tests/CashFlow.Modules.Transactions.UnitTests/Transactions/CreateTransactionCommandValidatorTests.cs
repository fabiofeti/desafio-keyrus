using CashFlow.Modules.Transactions.Application.Transactions.Create;
using FluentValidation.TestHelper;

namespace CashFlow.Modules.Transactions.UnitTests.Transactions;

public class CreateTransactionCommandValidatorTests
{
    private readonly CreateTransactionCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Pass_WhenCommandIsValid()
    {
        _validator.TestValidate(ValidCommand()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    public void Validate_Should_Fail_WhenIdempotencyKeyIsMissingOrMalformed(string key)
    {
        _validator.TestValidate(ValidCommand() with { IdempotencyKey = key })
            .ShouldHaveValidationErrorFor(c => c.IdempotencyKey);
    }

    [Fact]
    public void Validate_Should_Fail_WhenIdempotencyKeyIsLongerThan100()
    {
        _validator.TestValidate(ValidCommand() with { IdempotencyKey = new string('k', 101) })
            .ShouldHaveValidationErrorFor(c => c.IdempotencyKey);
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("1")]
    [InlineData("Transfer")]
    public void Validate_Should_Fail_WhenTypeIsNotCreditOrDebit(string type)
    {
        _validator.TestValidate(ValidCommand() with { Type = type })
            .ShouldHaveValidationErrorFor(c => c.Type);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.123")]
    [InlineData("1000000000.00")]
    public void Validate_Should_Fail_WhenAmountIsOutOfRange(string amount)
    {
        _validator.TestValidate(ValidCommand() with { Amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) })
            .ShouldHaveValidationErrorFor(c => c.Amount);
    }

    [Fact]
    public void Validate_Should_Pass_WhenAmountIsTheMaximum()
    {
        _validator.TestValidate(ValidCommand() with { Amount = CreateTransactionCommandValidator.MaxAmount })
            .ShouldNotHaveValidationErrorFor(c => c.Amount);
    }

    [Fact]
    public void Validate_Should_Fail_WhenOccurredAtIsMissing()
    {
        _validator.TestValidate(ValidCommand() with { OccurredAt = default })
            .ShouldHaveValidationErrorFor(c => c.OccurredAt);
    }

    [Fact]
    public void Validate_Should_Fail_WhenDescriptionIsLongerThan200()
    {
        _validator.TestValidate(ValidCommand() with { Description = new string('d', 201) })
            .ShouldHaveValidationErrorFor(c => c.Description);
    }

    private static CreateTransactionCommand ValidCommand() => new(
        "key_1-A", "Debit", 10.50m, new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(-3)), "Fornecedor");
}
