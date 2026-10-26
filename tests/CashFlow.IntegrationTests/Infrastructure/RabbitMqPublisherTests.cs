using System.Text;
using CashFlow.Common.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace CashFlow.IntegrationTests.Infrastructure;

/// <summary>
/// The shared publisher against a real broker: a confirmed publish lands as a
/// persistent message with its id, type and correlation id, and an unroutable
/// one fails instead of being silently dropped - which is what lets the outbox
/// publisher mark a row as published only after the broker took it.
/// </summary>
[Trait("Category", "Integration")]
public class RabbitMqPublisherTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Publish_Should_DeliverPersistentMessage_WhenRoutable()
    {
        // Arrange
        await using ServiceProvider services = BuildMessagingServices();
        IRabbitMqPublisher publisher = services.GetRequiredService<IRabbitMqPublisher>();
        IConnection connection = await services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(default);
        await using IChannel channel = await connection.CreateChannelAsync();

        string exchange = $"it.exchange.{Guid.NewGuid():N}";
        string queue = $"it.queue.{Guid.NewGuid():N}";
        await publisher.DeclareExchangeAsync(exchange, default);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: true);
        await channel.QueueBindAsync(queue, exchange, "probe.v1");

        var messageId = Guid.NewGuid();

        // Act
        await publisher.PublishAsync(
            new OutgoingMessage(exchange, "probe.v1", messageId, "it.probe.v1", "it-correlation", Encoding.UTF8.GetBytes("{}")),
            default);

        // Assert
        BasicGetResult? delivered = await channel.BasicGetAsync(queue, autoAck: true);
        delivered.ShouldNotBeNull();
        delivered.BasicProperties.Persistent.ShouldBeTrue();
        delivered.BasicProperties.MessageId.ShouldBe(messageId.ToString());
        delivered.BasicProperties.Type.ShouldBe("it.probe.v1");
        delivered.BasicProperties.CorrelationId.ShouldBe("it-correlation");
    }

    [Fact]
    public async Task Publish_Should_Throw_WhenNoQueueIsBound()
    {
        // Arrange
        await using ServiceProvider services = BuildMessagingServices();
        IRabbitMqPublisher publisher = services.GetRequiredService<IRabbitMqPublisher>();
        string exchange = $"it.exchange.{Guid.NewGuid():N}";
        await publisher.DeclareExchangeAsync(exchange, default);

        // Act + Assert: mandatory publish with nowhere to go is returned by the
        // broker, and the caller hears about it.
        await Should.ThrowAsync<PublishException>(() => publisher.PublishAsync(
            new OutgoingMessage(exchange, "nobody.listens", Guid.NewGuid(), "it.probe.v1", "it-correlation", Encoding.UTF8.GetBytes("{}")),
            default));
    }

    private ServiceProvider BuildMessagingServices()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(Factory.RabbitMqSettings)
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddMessaging(configuration)
            .BuildServiceProvider();
    }
}
