using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CashFlow.IntegrationTests.Consolidation;

/// <summary>
/// GET /daily-balances/{date} by HTTP, with the state built by the real pipeline
/// (POST → Outbox → RabbitMQ → Inbox). Expected totals come from what the test sent.
/// </summary>
[Trait("Category", "Integration")]
public class DailyBalancesTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan Convergence = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Get_Should_ConvergeToTheTotalsOfTheDay_AfterTheWorkerConsolidates()
    {
        // Arrange
        await using WorkerHost worker = await WorkerHost.StartAsync(Factory);
        DateOnly date = UniqueDate();
        (string Type, decimal Amount)[] entries = [("Credit", 125.50m), ("Debit", 20.00m), ("Credit", 0.05m)];
        decimal credits = entries.Where(e => e.Type == "Credit").Sum(e => e.Amount);
        decimal debits = entries.Where(e => e.Type == "Debit").Sum(e => e.Amount);

        // Act
        foreach ((string type, decimal amount) in entries)
        {
            (await PostAsync(type, amount, date)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // Assert
        BalanceDto balance = await EventuallyAsync(date, b => b.Credits == credits && b.Debits == debits);
        balance.Date.ShouldBe(date);
        balance.Balance.ShouldBe(credits - debits);
        balance.LastUpdatedAt.ShouldNotBeNull();
        balance.Consistency.ShouldBe("eventual");
    }

    [Fact]
    public async Task Get_Should_Return200WithZeros_WhenTheDayHasNoConsolidatedTransactions()
    {
        // Arrange
        DateOnly date = UniqueDate();

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"daily-balances/{date:yyyy-MM-dd}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<BalanceDto>(Json))
            .ShouldBe(new BalanceDto(date, 0m, 0m, 0m, null, "eventual"));
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("26-10-2026")]
    [InlineData("today")]
    public async Task Get_Should_Return400_WhenTheDateIsNotAValidDate(string date)
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"daily-balances/{date}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed record BalanceDto(
        DateOnly Date, decimal Credits, decimal Debits, decimal Balance, DateTime? LastUpdatedAt, string Consistency);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static int _dayOffset;

    /// <summary>The database is shared by the whole run; each test owns its day.</summary>
    private static DateOnly UniqueDate() =>
        new DateOnly(2051, 1, 1).AddDays(Interlocked.Increment(ref _dayOffset) + Random.Shared.Next(0, 300) * 10);

    private Task<HttpResponseMessage> PostAsync(string type, decimal amount, DateOnly date)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "transactions")
        {
            Content = JsonContent.Create(new { type, amount, occurredAt = $"{date:yyyy-MM-dd}T12:00:00-03:00" })
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}");
        return HttpClient.SendAsync(request);
    }

    private async Task<BalanceDto> EventuallyAsync(DateOnly date, Func<BalanceDto, bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Convergence;
        while (true)
        {
            BalanceDto balance = (await HttpClient.GetFromJsonAsync<BalanceDto>($"daily-balances/{date:yyyy-MM-dd}", Json))!;
            if (condition(balance))
            {
                return balance;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, "the balance did not converge within the window");
            await Task.Delay(200);
        }
    }
}
