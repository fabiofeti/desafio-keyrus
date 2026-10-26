using System.Collections.Concurrent;
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
/// One background consumer per subscription. Declares the durable queue and its
/// binding, consumes with manual acknowledgement, and hands every delivery to
/// <see cref="IIntegrationEventHandler{TIntegrationEvent}"/> in a fresh DI scope.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>Handler returned</term><description>ACK - the handler already committed.</description></item>
/// <item><term>Handler threw</term><description>NACK with requeue, after a short backoff.</description></item>
/// <item><term>Unreadable or unexpected message</term><description>Reject without requeue and log. There is no
/// dead-letter queue in the MVP; that is a documented limitation.</description></item>
/// <item><term>Host stopping</term><description>No ACK; the broker redelivers once the channel closes.</description></item>
/// </list>
/// Deliveries on one channel are processed one at a time (the client's default
/// dispatch concurrency), so a backoff delays the queue instead of spinning on it.
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

    private readonly ConcurrentDictionary<string, int> _failures = new();

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
        await channel.QueueDeclareAsync(
            subscription.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
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
            _failures.TryRemove(messageId, out _);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave it unacknowledged so the broker redelivers it.
        }
        catch (Exception exception)
        {
            int failures = _failures.AddOrUpdate(messageId, 1, (_, count) => count + 1);
            TimeSpan delay = Backoff[Math.Min(failures, Backoff.Length) - 1];

            logger.LogWarning(
                exception,
                "Handling {MessageType} {MessageId} failed (attempt {Attempt}); requeueing in {Delay}",
                subscription.MessageType, messageId, failures, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down during the backoff: the channel closes and the broker redelivers.
            }
        }
    }

    private TEvent? TryRead(BasicDeliverEventArgs delivery, string messageId)
    {
        if (delivery.BasicProperties.Type != subscription.MessageType)
        {
            logger.LogError(
                "Rejecting message {MessageId} on {Queue}: type '{Type}' is not '{Expected}'",
                messageId, subscription.Queue, delivery.BasicProperties.Type, subscription.MessageType);
            return default;
        }

        try
        {
            TEvent? integrationEvent = JsonSerializer.Deserialize<TEvent>(
                delivery.Body.Span, OutboxMessage.PayloadSerializerOptions);

            if (integrationEvent is null)
            {
                logger.LogError("Rejecting message {MessageId} on {Queue}: empty body", messageId, subscription.Queue);
            }

            return integrationEvent;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Rejecting message {MessageId} on {Queue}: unreadable body", messageId, subscription.Queue);
            return default;
        }
    }
}
