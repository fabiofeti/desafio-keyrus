using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CashFlow.Common.Infrastructure.Messaging;
using CashFlow.Modules.Consolidation.Infrastructure;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace CashFlow.IntegrationTests.Consolidation;

/// <summary>
/// The instruments as the hosts wire them: one POST through the API, the
/// Worker's outbox and consumer, and each module's meter records its part.
/// Listeners are scoped to each host's own IMeterFactory, so other tests'
/// hosts do not leak into the counts.
/// </summary>
[Trait("Category", "Integration")]
public class MetricsTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task OnePost_Should_BeCounted_ByTheApiTheOutboxAndTheConsolidation()
    {
        // Arrange
        await using WorkerHost worker = await WorkerHost.StartAsync(Factory);
        using var api = new Recorder(Factory.Services.GetRequiredService<IMeterFactory>());
        using var background = new Recorder(worker.Services.GetRequiredService<IMeterFactory>());
        string date = $"{new DateOnly(2061, 1, 1).AddDays(Random.Shared.Next(0, 3000)):yyyy-MM-dd}";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, "transactions")
        {
            Content = JsonContent.Create(new { type = "Debit", amount = 12.34m, occurredAt = $"{date}T12:00:00-03:00" })
        };
        request.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}");
        (await HttpClient.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Created);

        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await HttpClient.GetFromJsonAsync<JsonElement>($"daily-balances/{date}")).GetProperty("debits").GetDecimal() != 12.34m)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the balance did not converge");
            await Task.Delay(200);
        }

        // Assert
        api.Sum("cashflow.transactions.created").ShouldBe(1);
        api.Tags("cashflow.transactions.created").ShouldContain(new KeyValuePair<string, object?>("type", "Debit"));
        background.Sum("cashflow.outbox.published").ShouldBeGreaterThanOrEqualTo(1);
        background.Sum("cashflow.consolidation.events_applied").ShouldBeGreaterThanOrEqualTo(1);
        background.Values("cashflow.consolidation.lag").ShouldNotBeEmpty();
        background.Values("cashflow.consolidation.lag").ShouldAllBe(seconds => seconds >= 0 && seconds < 30);
    }

    [Fact]
    public async Task UnreadableMessage_Should_BeCountedAsDeadLettered()
    {
        // Arrange
        await using WorkerHost worker = await WorkerHost.StartAsync(Factory);
        using var background = new Recorder(worker.Services.GetRequiredService<IMeterFactory>());
        IRabbitMqPublisher publisher = worker.Services.GetRequiredService<IRabbitMqPublisher>();
        var garbage = new OutgoingMessage(
            TransactionCreatedIntegrationEvent.Exchange, TransactionCreatedIntegrationEvent.RoutingKey,
            Guid.NewGuid(), TransactionCreatedIntegrationEvent.MessageType, "it-metrics", Encoding.UTF8.GetBytes("{ not json"));

        // Act: until the consumer has declared its queue the broker returns the
        // message as unroutable; publish again until it is routed.
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await publisher.PublishAsync(garbage, default);
                break;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }

        while (background.Sum("cashflow.messaging.dead_lettered") < 1)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dead-letter was not counted");
            await Task.Delay(200);
        }

        // Assert
        background.Tags("cashflow.messaging.dead_lettered")
            .ShouldContain(new KeyValuePair<string, object?>("reason", "unreadable"));

        // The DLQ is shared by the run, and the DLQ tests take its first message:
        // take ours back out.
        IConnection connection = await worker.Services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(default);
        await using IChannel channel = await connection.CreateChannelAsync();
        string dlq = $"{ConsolidationModule.TransactionCreatedQueue}.dlq";
        while ((await channel.BasicGetAsync(dlq, autoAck: true))?.BasicProperties.MessageId != garbage.MessageId.ToString())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline.AddSeconds(30), "our message never reached the DLQ");
            await Task.Delay(100);
        }
    }

    /// <summary>Records every CashFlow.* measurement made through one host's meter factory.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];
        private readonly Lock _gate = new();

        public Recorder(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name.StartsWith("CashFlow.", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i.Name, v, t.ToArray()));
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i.Name, v, t.ToArray()));
            _listener.Start();
        }

        public double Sum(string name) => Values(name).Sum();

        public List<double> Values(string name)
        {
            lock (_gate)
            {
                return [.. _measurements.Where(m => m.Name == name).Select(m => m.Value)];
            }
        }

        public List<KeyValuePair<string, object?>> Tags(string name)
        {
            lock (_gate)
            {
                return [.. _measurements.Where(m => m.Name == name).SelectMany(m => m.Tags)];
            }
        }

        private void Add(string name, double value, KeyValuePair<string, object?>[] tags)
        {
            lock (_gate)
            {
                _measurements.Add((name, value, tags));
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
