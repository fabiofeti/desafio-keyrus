using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace CashFlow.IntegrationTests.Transactions;

[Trait("Category", "Integration")]
public class TransactionsTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Create_Should_Return201_AndBeListedOnItsBusinessDate()
    {
        // Arrange: 23:30 in São Paulo is already the next day in UTC.
        DateOnly date = UniqueDate();
        var body = Body(occurredAt: $"{date:yyyy-MM-dd}T23:30:00-03:00");

        // Act
        HttpResponseMessage created = await PostAsync(body, NewKey());
        TransactionDto transaction = await ReadAsync<TransactionDto>(created);
        PageDto page = await ReadAsync<PageDto>(await HttpClient.GetAsync($"transactions?date={date:yyyy-MM-dd}"));

        // Assert
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location!.ToString().ShouldBe($"/transactions/{transaction.Id}");
        transaction.BusinessDate.ShouldBe(date);
        page.TotalCount.ShouldBe(1);
        page.Items.Single().Id.ShouldBe(transaction.Id);
        page.Items.Single().Amount.ShouldBe(125.50m);
    }

    [Fact]
    public async Task Create_Should_WriteExactlyOneOutboxMessage_InTheSameTransaction()
    {
        // Act
        TransactionDto transaction = await ReadAsync<TransactionDto>(await PostAsync(Body(), NewKey()));

        // Assert: unpublished, waiting for the Worker - the API never talks to the broker.
        (long count, long unpublished) = await OutboxRowsForAsync(transaction.Id);
        count.ShouldBe(1);
        unpublished.ShouldBe(1);
    }

    [Fact]
    public async Task Create_Should_ReturnSameBody_WhenRepeatedWithSameKey()
    {
        // Arrange
        string key = NewKey();

        // Act
        HttpResponseMessage first = await PostAsync(Body(), key);
        HttpResponseMessage second = await PostAsync(Body(), key);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains("Idempotency-Replayed").ShouldBeFalse();
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotency-Replayed").ShouldBe(["true"]);
        TransactionDto original = await ReadAsync<TransactionDto>(first);
        (await ReadAsync<TransactionDto>(second)).ShouldBe(original);
        (await OutboxRowsForAsync(original.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Create_Should_CreateOnce_WhenConcurrentRequestsShareAKey()
    {
        // Arrange: both requests can pass the replay lookup; the unique index decides.
        string key = NewKey();
        DateOnly date = UniqueDate();
        var body = Body(occurredAt: $"{date:yyyy-MM-dd}T12:00:00-03:00");

        // Act
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => PostAsync(body, key)));

        // Assert
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        Guid[] ids = await Task.WhenAll(responses.Select(async r => (await ReadAsync<TransactionDto>(r)).Id));
        ids.Distinct().Count().ShouldBe(1);
        PageDto page = await ReadAsync<PageDto>(await HttpClient.GetAsync($"transactions?date={date:yyyy-MM-dd}"));
        page.TotalCount.ShouldBe(1);
        (await OutboxRowsForAsync(ids[0])).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Create_Should_Return409_WhenKeyReusedWithDifferentPayload()
    {
        // Arrange
        string key = NewKey();
        await PostAsync(Body(), key);

        // Act
        HttpResponseMessage response = await PostAsync(Body(amount: 99m), key);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(response)).ShouldBe("Transactions.IdempotencyKeyReused");
    }

    [Fact]
    public async Task Create_Should_Return400_WhenIdempotencyKeyIsMissing()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("transactions", Body());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("Validation.General");
    }

    [Theory]
    [InlineData("Credit", "0")]
    [InlineData("Credit", "10.123")]
    [InlineData("Transfer", "10")]
    public async Task Create_Should_Return400_WhenPayloadIsInvalid(string type, string amount)
    {
        // Act
        HttpResponseMessage response = await PostAsync(
            Body(type: type, amount: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), NewKey());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Should_Return400_WhenBodyIsNotJson()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "transactions")
        {
            Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", NewKey());

        // Act
        HttpResponseMessage response = await HttpClient.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("Request.Invalid");
    }

    [Fact]
    public async Task List_Should_PageInOccurredAtOrder()
    {
        // Arrange: created out of order.
        DateOnly date = UniqueDate();
        foreach (int hour in new[] { 15, 9, 12 })
        {
            await PostAsync(Body(occurredAt: $"{date:yyyy-MM-dd}T{hour:00}:00:00-03:00"), NewKey());
        }

        // Act
        PageDto first = await ReadAsync<PageDto>(await HttpClient.GetAsync($"transactions?date={date:yyyy-MM-dd}&page=1&pageSize=2"));
        PageDto second = await ReadAsync<PageDto>(await HttpClient.GetAsync($"transactions?date={date:yyyy-MM-dd}&page=2&pageSize=2"));

        // Assert
        first.TotalCount.ShouldBe(3);
        first.Items.Select(i => i.OccurredAt.ToOffset(TimeSpan.FromHours(-3)).Hour).ShouldBe([9, 12]);
        second.Items.Select(i => i.OccurredAt.ToOffset(TimeSpan.FromHours(-3)).Hour).ShouldBe([15]);
    }

    [Fact]
    public async Task List_Should_Return200WithNoItems_WhenTheDateHasNoTransactions()
    {
        // Act
        PageDto page = await ReadAsync<PageDto>(await HttpClient.GetAsync($"transactions?date={UniqueDate():yyyy-MM-dd}"));

        // Assert
        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("date=26-10-2026")]
    [InlineData("")]
    [InlineData("date=2026-10-26&page=0")]
    [InlineData("date=2026-10-26&pageSize=201")]
    public async Task List_Should_Return400_WhenQueryIsInvalid(string queryString)
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"transactions?{queryString}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static int _dayOffset;

    /// <summary>The database is shared by the whole run; each test owns its day.</summary>
    private static DateOnly UniqueDate() =>
        new DateOnly(2031, 1, 1).AddDays(Interlocked.Increment(ref _dayOffset) + Random.Shared.Next(0, 300) * 10);

    private static string NewKey() => $"it-{Guid.NewGuid():N}";

    private static object Body(
        string type = "Credit", decimal amount = 125.50m, string occurredAt = "2026-10-26T10:00:00-03:00") =>
        new { type, amount, occurredAt, description = "Venda" };

    private Task<HttpResponseMessage> PostAsync(object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "transactions") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return HttpClient.SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString();

    private async Task<(long Count, long Unpublished)> OutboxRowsForAsync(Guid transactionId)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*), count(*) FILTER (WHERE published_on_utc IS NULL)
            FROM transactions.outbox_messages
            WHERE payload->>'transactionId' = @id
            """, connection);
        command.Parameters.AddWithValue("id", transactionId.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private sealed record TransactionDto(
        Guid Id, string Type, decimal Amount, DateTimeOffset OccurredAt, DateOnly BusinessDate, string? Description);

    private sealed record PageDto(IReadOnlyList<TransactionDto> Items, int Page, int PageSize, int TotalCount);
}
