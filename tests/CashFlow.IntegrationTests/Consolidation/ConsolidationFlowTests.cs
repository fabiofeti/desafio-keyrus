using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CashFlow.Common.Application.EventBus;
using CashFlow.Common.Application.Outbox;
using CashFlow.Common.Infrastructure.Messaging;
using CashFlow.Modules.Consolidation.Infrastructure;
using CashFlow.Modules.Transactions.Infrastructure.Outbox;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;

namespace CashFlow.IntegrationTests.Consolidation;

/// <summary>
/// Outbox → RabbitMQ → Inbox → daily balance, with real PostgreSQL and RabbitMQ.
/// Every expected total is computed here from the amounts this test sent.
/// </summary>
[Trait("Category", "Integration")]
public class ConsolidationFlowTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan Convergence = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Transactions_Should_ReachTheDailyBalance_ThroughOutboxBrokerAndInbox()
    {
        // Arrange
        await using WorkerHost worker = await WorkerHost.StartAsync(Factory);
        DateOnly date = UniqueDate();
        (string Type, decimal Amount)[] entries =
            [("Credit", 125.50m), ("Credit", 74.50m), ("Debit", 20.00m), ("Credit", 0.01m), ("Debit", 99.99m)];

        // Act: concurrently, as clients would.
        Guid[] transactionIds = await Task.WhenAll(entries.Select(async e =>
        {
            HttpResponseMessage response = await PostAsync(e.Type, e.Amount, date);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }));

        // Assert
        decimal credits = entries.Where(e => e.Type == "Credit").Sum(e => e.Amount);
        decimal debits = entries.Where(e => e.Type == "Debit").Sum(e => e.Amount);
        await EventuallyAsync(async () => await BalanceAsync(date) == (credits, debits));

        Guid[] eventIds = await OutboxEventIdsAsync(transactionIds);
        eventIds.Length.ShouldBe(entries.Length);
        (await ScalarAsync("SELECT count(*) FROM consolidation.inbox_messages WHERE event_id = ANY(@ids)", eventIds))
            .ShouldBe(entries.Length);
        (await ScalarAsync("SELECT count(*) FROM transactions.outbox_messages WHERE id = ANY(@ids) AND published_on_utc IS NOT NULL", eventIds))
            .ShouldBe(entries.Length);
    }

    [Fact]
    public async Task Redelivery_Should_NotChangeTheBalance_WhenTheSameEventArrivesTwice()
    {
        // Arrange
        await using WorkerHost worker = await WorkerHost.StartAsync(Factory);
        IRabbitMqPublisher publisher = worker.Services.GetRequiredService<IRabbitMqPublisher>();
        DateOnly date = UniqueDate();
        TransactionCreatedIntegrationEvent duplicated = Event(date, "Credit", 40m);
        TransactionCreatedIntegrationEvent sentinel = Event(date, "Debit", 15m);

        // Act: the same event twice through the broker, then a different one. One
        // channel processes in order, so once the sentinel is applied both copies were seen.
        await publisher.PublishAsync(ToMessage(duplicated), default);
        await publisher.PublishAsync(ToMessage(duplicated), default);
        await publisher.PublishAsync(ToMessage(sentinel), default);

        // Assert
        await EventuallyAsync(async () => (await BalanceAsync(date)).Debits == 15m);
        (await BalanceAsync(date)).ShouldBe((40m, 15m));
        (await ScalarAsync("SELECT count(*) FROM consolidation.inbox_messages WHERE event_id = ANY(@ids)", [duplicated.Id]))
            .ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentEvents_Should_SumExactly_AndApplyEachEventOnce()
    {
        // Arrange: the handler pipeline the consumer uses, called in parallel.
        await using WorkerHost worker = WorkerHost.Create(Factory);
        DateOnly date = UniqueDate();
        TransactionCreatedIntegrationEvent[] distinct =
        [
            .. Enumerable.Range(1, 30).Select(i => Event(date, i % 3 == 0 ? "Debit" : "Credit", i + 0.25m))
        ];
        TransactionCreatedIntegrationEvent repeated = distinct[0];

        // Act: 30 different events plus 10 extra concurrent deliveries of one of them.
        await Task.WhenAll(distinct.Concat(Enumerable.Repeat(repeated, 10)).Select(async e =>
        {
            await using AsyncServiceScope scope = worker.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<IIntegrationEventHandler<TransactionCreatedIntegrationEvent>>()
                .Handle(e);
        }));

        // Assert
        decimal credits = distinct.Where(e => e.Type == "Credit").Sum(e => e.Amount);
        decimal debits = distinct.Where(e => e.Type == "Debit").Sum(e => e.Amount);
        (await BalanceAsync(date)).ShouldBe((credits, debits));
        (await ScalarAsync("SELECT count(*) FROM consolidation.inbox_messages WHERE event_id = ANY(@ids)", [.. distinct.Select(e => e.Id)]))
            .ShouldBe(distinct.Length);
    }

    [Fact]
    public async Task Publisher_Should_MarkPublishedOnlyAfterConfirm_WhenBrokerIsUnreachableAndThenBack()
    {
        // Arrange: a running Worker declares the consumer's queue (without it the
        // broker refuses the publish as unroutable - which also keeps the row
        // unpublished, but is not what this test is about) and drains what other
        // tests left in the outbox, so ours is the oldest row.
        await using (WorkerHost drain = await WorkerHost.StartAsync(Factory))
        {
            await EventuallyAsync(() => QueueExistsAsync(drain, ConsolidationModule.TransactionCreatedQueue));
            await EventuallyAsync(async () =>
                await ScalarAsync("SELECT count(*) FROM transactions.outbox_messages WHERE published_on_utc IS NULL", []) == 0);
        }

        HttpResponseMessage created = await PostAsync("Credit", 10m, UniqueDate());
        Guid transactionId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid eventId = (await OutboxEventIdsAsync([transactionId])).Single();

        // Nothing listens on port 1: every attempt fails at connect time.
        await using WorkerHost unreachable = WorkerHost.Create(
            Factory, new Dictionary<string, string?> { ["RabbitMq:Port"] = "1" });
        await using WorkerHost reachable = WorkerHost.Create(Factory);

        // Act
        OutboxCycle failedCycle = await unreachable.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(default);
        (int attemptsAfterFailure, bool publishedAfterFailure, bool hasError) = await OutboxRowAsync(eventId);

        OutboxCycle okCycle = await reachable.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(default);
        (int attemptsAfterSuccess, bool publishedAfterSuccess, bool stillHasError) = await OutboxRowAsync(eventId);

        // Assert
        failedCycle.Failed.ShouldBeTrue();
        failedCycle.Published.ShouldBe(0);
        publishedAfterFailure.ShouldBeFalse();
        attemptsAfterFailure.ShouldBe(1);
        hasError.ShouldBeTrue();

        okCycle.Failed.ShouldBeFalse();
        okCycle.Published.ShouldBeGreaterThanOrEqualTo(1);
        publishedAfterSuccess.ShouldBeTrue();
        attemptsAfterSuccess.ShouldBe(2);
        stillHasError.ShouldBeFalse();
    }

    [Fact]
    public async Task PoisonMessage_Should_BeDeadLettered_AndNotBlockTheMessagesBehindIt()
    {
        // Arrange: a malformed event (amount 0) that the module refuses every
        // time, followed by a valid one for the same day.
        await using WorkerHost worker = await WorkerHost.StartAsync(Factory);
        IRabbitMqPublisher publisher = worker.Services.GetRequiredService<IRabbitMqPublisher>();
        DateOnly date = UniqueDate();
        TransactionCreatedIntegrationEvent poison = Event(date, "Credit", 0m);
        TransactionCreatedIntegrationEvent behind = Event(date, "Credit", 33m);
        await EventuallyAsync(() => QueueExistsAsync(worker, ConsolidationModule.TransactionCreatedQueue));

        // Act
        await publisher.PublishAsync(ToMessage(poison), default);
        await publisher.PublishAsync(ToMessage(behind), default);

        // Assert: the one behind is applied, the poison is parked, never applied.
        await EventuallyAsync(async () => await BalanceAsync(date) == (33m, 0m));
        BasicGetResult parked = await EventuallyGetAsync(worker, DeadLetterQueue);
        parked.BasicProperties.MessageId.ShouldBe(poison.Id.ToString());
        (await ScalarAsync("SELECT count(*) FROM consolidation.inbox_messages WHERE event_id = ANY(@ids)", [poison.Id]))
            .ShouldBe(0);
        (await BalanceAsync(date)).ShouldBe((33m, 0m));
    }

    [Fact]
    public async Task UnreadableMessage_Should_BeDeadLetteredAtOnce()
    {
        // Arrange
        await using WorkerHost worker = await WorkerHost.StartAsync(Factory);
        IRabbitMqPublisher publisher = worker.Services.GetRequiredService<IRabbitMqPublisher>();
        await EventuallyAsync(() => QueueExistsAsync(worker, ConsolidationModule.TransactionCreatedQueue));
        var messageId = Guid.NewGuid();

        // Act: right routing key, body that is not the contract.
        await publisher.PublishAsync(
            new OutgoingMessage(
                TransactionCreatedIntegrationEvent.Exchange,
                TransactionCreatedIntegrationEvent.RoutingKey,
                messageId,
                TransactionCreatedIntegrationEvent.MessageType,
                "it-correlation",
                Encoding.UTF8.GetBytes("{ not json")),
            default);

        // Assert: parked, not dropped, and not retried first.
        BasicGetResult parked = await EventuallyGetAsync(worker, DeadLetterQueue);
        parked.BasicProperties.MessageId.ShouldBe(messageId.ToString());
    }

    private static string DeadLetterQueue => $"{ConsolidationModule.TransactionCreatedQueue}.dlq";

    private static async Task<bool> QueueExistsAsync(WorkerHost worker, string queue)
    {
        IConnection connection = await worker.Services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(default);
        await using IChannel channel = await connection.CreateChannelAsync();
        try
        {
            await channel.QueueDeclarePassiveAsync(queue);
            return true;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return false;
        }
    }

    /// <summary>Takes (and acknowledges) the first message the queue receives within the window.</summary>
    private static async Task<BasicGetResult> EventuallyGetAsync(WorkerHost worker, string queue)
    {
        IConnection connection = await worker.Services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(default);
        await using IChannel channel = await connection.CreateChannelAsync();
        BasicGetResult? result = null;
        await EventuallyAsync(async () => (result = await channel.BasicGetAsync(queue, autoAck: true)) is not null);
        return result!;
    }

    private static int _dayOffset;

    /// <summary>Each test owns its business day; the database is shared by the run.</summary>
    private static DateOnly UniqueDate() =>
        new DateOnly(2041, 1, 1).AddDays(Interlocked.Increment(ref _dayOffset) + Random.Shared.Next(0, 300) * 10);

    private static TransactionCreatedIntegrationEvent Event(DateOnly date, string type, decimal amount) => new(
        Guid.CreateVersion7(), DateTime.UtcNow, Guid.CreateVersion7(), type, amount,
        date, new DateTimeOffset(date, new TimeOnly(12, 0), TimeSpan.FromHours(-3)),
        TransactionCreatedIntegrationEvent.CurrentVersion, "it-correlation");

    private static OutgoingMessage ToMessage(TransactionCreatedIntegrationEvent e) => new(
        TransactionCreatedIntegrationEvent.Exchange,
        TransactionCreatedIntegrationEvent.RoutingKey,
        e.Id,
        TransactionCreatedIntegrationEvent.MessageType,
        e.CorrelationId,
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e, OutboxMessage.PayloadSerializerOptions)));

    private Task<HttpResponseMessage> PostAsync(string type, decimal amount, DateOnly date)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "transactions")
        {
            Content = JsonContent.Create(new { type, amount, occurredAt = $"{date:yyyy-MM-dd}T12:00:00-03:00" })
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}");
        return HttpClient.SendAsync(request);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow + Convergence;
        while (!await condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the condition did not hold within the convergence window");
            await Task.Delay(200);
        }
    }

    private async Task<(decimal Credits, decimal Debits)> BalanceAsync(DateOnly date)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT credits, debits FROM consolidation.daily_balances WHERE business_date = @date", connection);
        command.Parameters.AddWithValue("date", date);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetDecimal(0), reader.GetDecimal(1)) : (0m, 0m);
    }

    private async Task<Guid[]> OutboxEventIdsAsync(Guid[] transactionIds)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM transactions.outbox_messages WHERE (payload->>'transactionId')::uuid = ANY(@ids)", connection);
        command.Parameters.AddWithValue("ids", transactionIds);
        var ids = new List<Guid>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }

    private async Task<(int Attempts, bool Published, bool HasError)> OutboxRowAsync(Guid eventId)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT attempt_count, published_on_utc IS NOT NULL, last_error IS NOT NULL FROM transactions.outbox_messages WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", eventId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private async Task<long> ScalarAsync(string sql, Guid[] ids, DateOnly? date = null)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", ids);
        if (date is not null)
        {
            command.Parameters.AddWithValue("date", date.Value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
