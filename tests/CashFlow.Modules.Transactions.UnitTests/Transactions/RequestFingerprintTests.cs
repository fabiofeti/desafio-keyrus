using CashFlow.Modules.Transactions.Application.Transactions.Create;

namespace CashFlow.Modules.Transactions.UnitTests.Transactions;

public class RequestFingerprintTests
{
    private static readonly CreateTransactionCommand Command = new(
        "key-1", "Credit", 10.5m, new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(-3)), "Venda");

    [Fact]
    public void Of_Should_IgnoreTheKey_AndNormaliseAmountAndInstant()
    {
        // Arrange: another key, a trailing zero and the same instant in UTC.
        CreateTransactionCommand equivalent = Command with
        {
            IdempotencyKey = "key-2",
            Amount = 10.50m,
            OccurredAt = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero)
        };

        // Act & Assert
        RequestFingerprint.Of(equivalent).ShouldBe(RequestFingerprint.Of(Command));
    }

    [Fact]
    public void Of_Should_Differ_WhenAnyPayloadFieldChanges()
    {
        // Arrange
        string original = RequestFingerprint.Of(Command);

        // Act & Assert
        RequestFingerprint.Of(Command with { Type = "Debit" }).ShouldNotBe(original);
        RequestFingerprint.Of(Command with { Amount = 10.51m }).ShouldNotBe(original);
        RequestFingerprint.Of(Command with { OccurredAt = Command.OccurredAt.AddSeconds(1) }).ShouldNotBe(original);
        RequestFingerprint.Of(Command with { Description = "Outra" }).ShouldNotBe(original);
    }

    [Fact]
    public void Of_Should_BeA64CharacterHexString()
    {
        RequestFingerprint.Of(Command).ShouldMatch("^[0-9a-f]{64}$");
    }
}
