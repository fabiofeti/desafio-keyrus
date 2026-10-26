using Polly;
using RabbitMQ.Client;

namespace CashFlow.Common.Infrastructure.Messaging;

/// <summary>
/// Publishes on a single channel with publisher confirms and confirmation
/// tracking on: <c>BasicPublishAsync</c> then completes only after the broker's
/// ack, and a nack or an unroutable return (mandatory) surfaces as a
/// <see cref="RabbitMQ.Client.Exceptions.PublishException"/>.
/// </summary>
/// <remarks>
/// An <see cref="IChannel"/> is not safe for concurrent publishing, hence the
/// lock. Any failure discards the channel; the next attempt opens a new one.
/// </remarks>
internal sealed class RabbitMqPublisher(RabbitMqConnectionProvider connections) : IRabbitMqPublisher, IAsyncDisposable
{
    private static readonly CreateChannelOptions ConfirmedChannel = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ResiliencePipeline _publish = RabbitMqResilience.CreatePublishPipeline();
    private IChannel? _channel;

    public Task DeclareExchangeAsync(string exchange, CancellationToken cancellationToken) =>
        WithChannelAsync(
            (channel, token) => channel.ExchangeDeclareAsync(
                exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: token),
            cancellationToken);

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.MessageId.ToString(),
            Type = message.Type,
            CorrelationId = message.CorrelationId,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await _publish.ExecuteAsync(
            async token => await WithChannelAsync(
                (channel, innerToken) => channel.BasicPublishAsync(
                    message.Exchange,
                    message.RoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: message.Body,
                    cancellationToken: innerToken).AsTask(),
                token),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _lock.Dispose();
    }

    private async Task WithChannelAsync(Func<IChannel, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                IConnection connection = await connections.GetConnectionAsync(cancellationToken);
                _channel = await connection.CreateChannelAsync(ConfirmedChannel, cancellationToken);
            }

            await action(_channel, cancellationToken);
        }
        catch
        {
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            await _channel.DisposeAsync();
        }
        catch (Exception)
        {
            // The channel is already broken; disposing it is best effort.
        }

        _channel = null;
    }
}
