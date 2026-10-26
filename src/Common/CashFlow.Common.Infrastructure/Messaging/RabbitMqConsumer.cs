using System.Text.Json;
using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Application.EventBus;
using CashFlow.Common.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CashFlow.Common.Infrastructure.Messaging;

/// <summary>
/// One background consumer per subscription. Declares the topology, consumes
/// with manual acknowledgement, and hands every delivery to
/// <see cref="IIntegrationEventHandler{TIntegrationEvent}"/> in a fresh DI scope.
/// </summary>
/// <remarks>
/// <para>
/// The queue is a quorum queue with a delivery limit and a dead-letter exchange:
/// the broker counts the returns (the count survives a Worker restart) and, past
/// the limit, moves the message to <see cref="SubscriptionOptions{TEvent}.DeadLetterQueue"/>,
/// so one message that always fails cannot block the queue for ever.
/// Dead-lettering is at-least-once, so a message is not lost on its way there.
/// </para>
/// <list type="table">
/// <item><term>Handler returned</term><description>ACK - the handler already committed.</description></item>
/// <item><term>Handler threw</term><description>Reject with requeue, after a backoff; past the limit, the broker dead-letters it.</description></item>
/// <item><term>Unreadable or unexpected message</term><description>Reject without requeue: dead-lettered at once, logged.</description></item>
/// <item><term>Host stopping</term><description>No ACK; the broker redelivers once the channel closes.</description></item>
/// </list>
/// Deliveries on one channel are processed one at a time (the client's default
/// dispatch concurrency), so a backoff delays the queue instead of spinning on it.
/// Replaying the dead-letter queue is a manual operation (management UI).
/// </remarks>
internal sealed class RabbitMqConsumer<TEvent>(
    RabbitMqConnectionProvider connections,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    SubscriptionOptions<TEvent> subscription,
    ILogger<RabbitMqConsumer<TEvent>> logger) : BackgroundService
    where TEvent : IIntegrationEvent
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    /// <summary>Direct exchange shared by every subscription; each DLQ is bound by its queue's name.</summary>
    public const string DeadLetterExchange = "cashflow.dead-letter";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilChannelClosesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Consumer for {Queue} lost its channel; reconnecting", subscription.Queue);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConsumeUntilChannelClosesAsync(CancellationToken stoppingToken)
    {
        IConnection connection = await connections.GetConnectionAsync(stoppingToken);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(
            subscription.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(
            DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            subscription.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            subscription.DeadLetterQueue, DeadLetterExchange, subscription.Queue, cancellationToken: stoppingToken);

        // A queue declared earlier with other arguments (a classic queue from
        // before this declaration) fails here with PRECONDITION_FAILED and must
        // be recreated once; see the operations notes in the README.
        await channel.QueueDeclareAsync(
            subscription.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: QueueArguments(),
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            subscription.Queue, subscription.Exchange, subscription.RoutingKey, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, global: false, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(subscription.Queue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("Consuming {MessageType} from {Queue}", subscription.MessageType, subscription.Queue);

        await closed.Task.WaitAsync(stoppingToken);
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        string messageId = delivery.BasicProperties.MessageId ?? $"tag-{delivery.DeliveryTag}";

        TEvent? integrationEvent = TryRead(delivery, messageId);
        if (integrationEvent is null)
        {
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<CorrelationContext>()
                .Set(delivery.BasicProperties.CorrelationId ?? integrationEvent.Id.ToString("N"));

            IIntegrationEventHandler<TEvent> handler =
                scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();

            await handler.Handle(integrationEvent, stoppingToken);

            // Only after the handler returned, which means its effect is committed.
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave it unacknowledged so the broker redelivers it.
        }
        catch (Exception exception)
        {
            // The broker's count of earlier returns: 0 on the first delivery.
            long returns = DeliveryCount(delivery);
            TimeSpan delay = Backoff[(int)Math.Min(returns, Backoff.Length - 1)];

            logger.LogWarning(
                exception,
                "Handling {MessageType} {MessageId} failed (delivery {Delivery} of {Limit}); returning it in {Delay}",
                subscription.MessageType, messageId, returns + 1, options.Value.DeliveryLimit + 1, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
                // Reject, not Nack: in RabbitMQ 4 quorum queues only basic.reject
                // (and a lost channel) counts towards x-delivery-limit; basic.nack
                // with requeue returns the message without counting, for ever.
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: true, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down during the backoff: the channel closes and the broker redelivers.
            }
        }
    }

    private Dictionary<string, object?> QueueArguments() => new()
    {
        ["x-queue-type"] = "quorum",
        ["x-delivery-limit"] = options.Value.DeliveryLimit,
        ["x-dead-letter-exchange"] = DeadLetterExchange,
        ["x-dead-letter-routing-key"] = subscription.Queue,
        // At-least-once dead-lettering needs reject-publish overflow; the queue
        // has no length limit, so the overflow setting never refuses a publish.
        ["x-dead-letter-strategy"] = "at-least-once",
        ["x-overflow"] = "reject-publish"
    };

    private static long DeliveryCount(BasicDeliverEventArgs delivery) =>
        delivery.BasicProperties.Headers?.TryGetValue("x-delivery-count", out object? value) == true && value is not null
            ? Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

    private TEvent? TryRead(BasicDeliverEventArgs delivery, string messageId)
    {
        if (delivery.BasicProperties.Type != subscription.MessageType)
        {
            logger.LogError(
                "Dead-lettering message {MessageId} from {Queue}: type '{Type}' is not '{Expected}'",
                messageId, subscription.Queue, delivery.BasicProperties.Type, subscription.MessageType);
            return default;
        }

        try
        {
            TEvent? integrationEvent = JsonSerializer.Deserialize<TEvent>(
                delivery.Body.Span, OutboxMessage.PayloadSerializerOptions);

            if (integrationEvent is null)
            {
                logger.LogError("Dead-lettering message {MessageId} from {Queue}: empty body", messageId, subscription.Queue);
            }

            return integrationEvent;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Dead-lettering message {MessageId} from {Queue}: unreadable body", messageId, subscription.Queue);
            return default;
        }
    }
}
