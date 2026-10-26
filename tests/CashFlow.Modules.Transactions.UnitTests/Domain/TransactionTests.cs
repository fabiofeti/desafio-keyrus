using CashFlow.Common.Domain;
using CashFlow.Modules.Transactions.Domain.Transactions;

namespace CashFlow.Modules.Transactions.UnitTests.Domain;

public class TransactionTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 26, 21, 30, 0, TimeSpan.FromHours(-3));
    private static readonly DateOnly BusinessDate = new(2026, 10, 26);
    private static readonly DateTime NowUtc = new(2026, 10, 27, 0, 31, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Should_ReturnTransaction_WhenValid()
    {
        // Act
        Result<Transaction> result = Create(TransactionType.Credit, 150.25m);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Transaction transaction = result.Value;
        transaction.Id.ShouldNotBe(Guid.Empty);
        transaction.Type.ShouldBe(TransactionType.Credit);
        transaction.Amount.ShouldBe(150.25m);
        transaction.BusinessDate.ShouldBe(BusinessDate);
        transaction.IdempotencyKey.ShouldBe("key-1");
        transaction.CreatedAtUtc.ShouldBe(NowUtc);
    }

    [Fact]
    public void Create_Should_KeepTheInstant_InUtc()
    {
        // Act
        Transaction transaction = Create(TransactionType.Debit, 10m).Value;

        // Assert: same instant, offset zero, so PostgreSQL's timestamptz accepts it.
        transaction.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        transaction.OccurredAt.ShouldBe(OccurredAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Should_Fail_WhenAmountIsNotPositive(decimal amount)
    {
        // Act
        Result<Transaction> result = Create(TransactionType.Debit, amount);

        // Assert
        result.Error.ShouldBe(TransactionErrors.AmountMustBePositive);
    }

    [Fact]
    public void Create_Should_Fail_WhenAmountHasMoreThanTwoDecimals()
    {
        // Act
        Result<Transaction> result = Create(TransactionType.Credit, 1.001m);

        // Assert
        result.Error.ShouldBe(TransactionErrors.AmountPrecision);
    }

    [Fact]
    public void Create_Should_Fail_WhenTypeIsUnknown()
    {
        // Act
        Result<Transaction> result = Create((TransactionType)99, 1m);

        // Assert
        result.Error.ShouldBe(TransactionErrors.UnknownType);
    }

    [Fact]
    public void Create_Should_GenerateDistinctIds()
    {
        // Act
        Guid first = Create(TransactionType.Credit, 1m).Value.Id;
        Guid second = Create(TransactionType.Credit, 1m).Value.Id;

        // Assert
        first.ShouldNotBe(second);
    }

    private static Result<Transaction> Create(TransactionType type, decimal amount) =>
        Transaction.Create(type, amount, OccurredAt, BusinessDate, "sale", "key-1", new string('a', 64), NowUtc);
}
