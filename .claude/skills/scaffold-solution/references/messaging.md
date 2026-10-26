# Mensageria em Common.Infrastructure (RabbitMQ.Client 7 + Polly 8)

Base genérica que os módulos usam. Topologia específica (exchange, fila, routing
key) é decidida pelo módulo dono do contrato e pelo módulo consumidor, no
`add-integration-event`. Aqui ficam só as peças sem conhecimento de domínio.

## Opções

```csharp
namespace CashFlow.Common.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string Username { get; init; } = string.Empty;   // via env, nunca no appsettings
    public string Password { get; init; } = string.Empty;   // via env, nunca no appsettings
    public string VirtualHost { get; init; } = "/";
    public ushort PrefetchCount { get; init; } = 16;
}
```

Validar no startup (`ValidateOnStart`) que `Username` e `Password` vieram preenchidos.

## Conexão

`RabbitMqConnectionProvider` (singleton): cria uma `IConnection` preguiçosamente
com `ConnectionFactory { AutomaticRecoveryEnabled = true, TopologyRecoveryEnabled = true,
ClientProvidedName = "cashflow-worker" }` e `await factory.CreateConnectionAsync(ct)`,
envolvida no pipeline `rabbitmq-connect` (retry exponencial com jitter, sem limite
de tentativas no startup, mas com teto de 30 s entre elas e respeitando o
`CancellationToken`). Descartar no `DisposeAsync`.

## Pipelines Polly

```csharp
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using RabbitMQ.Client.Exceptions;

internal static class RabbitMqResilience
{
    public const string Publish = "rabbitmq-publish";

    // Retry é seguro aqui porque publicar de novo é idempotente para o sistema:
    // o consumidor deduplica pelo eventId na Inbox. Isso NÃO vale para o
    // SaveChanges de um lançamento, que não recebe retry.
    private static readonly PredicateBuilder Transient = new PredicateBuilder()
        .Handle<BrokerUnreachableException>()
        .Handle<AlreadyClosedException>()
        .Handle<OperationInterruptedException>()
        .Handle<PublishException>()          // nack ou mensagem devolvida
        .Handle<TimeoutRejectedException>()
        .Handle<IOException>();

    public static ResiliencePipeline CreatePublishPipeline() => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = Transient,
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(200)
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ShouldHandle = Transient,
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15)
        })
        // Por último = mais interno: o timeout vale por tentativa.
        .AddTimeout(TimeSpan.FromSeconds(5))
        .Build();
}
```

Registrar com `services.AddResiliencePipeline(...)` se usar
`Polly.Extensions`, ou como singleton nomeado. O circuit breaker vive **só** no
Worker: quando abre, o publisher espera `BreakDuration` e tenta de novo; a API
nunca vê esse estado.

## Publisher

```csharp
public interface IRabbitMqPublisher
{
    /// <summary>
    /// Retorna somente depois do confirm do broker. Lança se o broker recusou,
    /// devolveu a mensagem (mandatory) ou não respondeu dentro do timeout.
    /// </summary>
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}

public sealed record OutgoingMessage(
    string Exchange,
    string RoutingKey,
    Guid MessageId,
    string Type,
    string CorrelationId,
    ReadOnlyMemory<byte> Body);
```

Implementação: um canal criado com
`new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true)`,
`BasicProperties { Persistent = true, ContentType = "application/json", MessageId, Type, CorrelationId }`
e `await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, props, body, ct)`
dentro do pipeline `rabbitmq-publish`. Com tracking ligado o `await` só volta
depois do ack; nack ou retorno viram `PublishException`. Proteja o canal com
`SemaphoreSlim(1)`: `IChannel` não é thread-safe para publicação. Se o canal
fechar, recrie na próxima chamada.

## Consumer genérico

```csharp
/// <summary>
/// Um BackgroundService por assinatura. Declara a fila durável, liga ao exchange,
/// consome com autoAck: false e prefetch configurado, e para cada entrega abre um
/// escopo de DI, desserializa e chama IIntegrationEventHandler{TEvent}.
/// </summary>
internal sealed class RabbitMqConsumer<TEvent>(
    RabbitMqConnectionProvider connections,
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    SubscriptionOptions<TEvent> subscription,
    ILogger<RabbitMqConsumer<TEvent>> logger) : BackgroundService
    where TEvent : IIntegrationEvent
```

Regras do loop de entrega (`AsyncEventingBasicConsumer.ReceivedAsync`):

| Situação | Ação |
|---|---|
| Handler retornou | `BasicAckAsync` — o handler já commitou |
| Handler lançou exceção | `BasicNackAsync(requeue: true)` depois de um atraso curto com backoff (ex.: 1 s, 2 s, 5 s pelo header de tentativas que o consumer mantém em memória por `MessageId`) |
| Corpo não desserializa / `Type` desconhecido | `BasicRejectAsync(requeue: false)` + log de erro com `MessageId`. Sem DLQ no MVP: é uma limitação documentada (DLQ é P2) |
| Cancelamento do host | Não faz ACK; a mensagem volta para a fila quando o canal fecha |

O correlation id da mensagem é colocado no escopo (`ICorrelationIdProvider`)
antes de chamar o handler, para que os logs do processamento carreguem o mesmo id.

Registro, usado pelo módulo consumidor:

```csharp
services.AddIntegrationEventConsumer<TransactionCreatedIntegrationEvent>(new SubscriptionOptions<TransactionCreatedIntegrationEvent>(
    Exchange: TransactionCreatedIntegrationEvent.Exchange,
    RoutingKey: TransactionCreatedIntegrationEvent.RoutingKey,
    Queue: "consolidation.transaction-created.v1",
    MessageType: TransactionCreatedIntegrationEvent.MessageType));
```

## Topologia

Quem publica declara o exchange (`topic`, durável). Quem consome declara a fila
(durável, `x-queue-type: quorum` é opcional; `classic` basta no MVP) e o binding.
As duas declarações são idempotentes, então tanto publisher quanto consumer
podem rodar primeiro.
