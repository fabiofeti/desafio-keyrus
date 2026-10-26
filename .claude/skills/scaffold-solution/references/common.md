# Templates da camada Common

Use quando a origem no CoreMastery não estiver disponível, e para os pontos que
mudam em relação a ela. Namespace raiz: `CashFlow`.

## Common.Domain

```csharp
namespace CashFlow.Common.Domain;

/// <summary>
/// Base de agregados. Sem coleção de domain events: neste projeto o único fato
/// que sai de um módulo é uma integration event escrita na Outbox, de forma explícita.
/// </summary>
public abstract class Entity
{
    protected Entity()
    {
    }
}
```

`Result`, `Result<T>`, `Error` (`Failure`, `Validation`, `Problem`, `NotFound`,
`Conflict`) e `ValidationError` são copiados como estão, só com o namespace
`CashFlow.Common.Domain`. O csproj de Common.Domain não referencia nada.

## Common.Application

Pastas: `Messaging/` (`IRequest`, `ICommand`, `ICommand<T>`, `IQuery<T>`,
`ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`), `Decorators/`
(validação, logging, exceções; todos `internal sealed` aninhados), `Abstractions/`
(`IDateTimeProvider`, `IUnitOfWork`, `ICorrelationIdProvider`), `EventBus/`
(`IIntegrationEvent`, `IntegrationEvent`, `IIntegrationEventHandler<T>`), `Outbox/`.

Namespaces: `CashFlow.Common.Application.Messaging`, `…Abstractions`,
`…EventBus`, `…Outbox`. Ordem dos decorators, do mais externo ao handler:
exceção → logging → validação → handler.

```csharp
namespace CashFlow.Common.Application.EventBus;

/// <summary>
/// Quem consome um evento de outro módulo. Fica na Presentation do módulo
/// consumidor: ouvir o vizinho é uma entrada de fora, como um endpoint.
/// Lançar exceção significa "não processei": o consumer faz NACK e o broker
/// reentrega. Retornar significa "commitado": o consumer faz ACK.
/// </summary>
public interface IIntegrationEventHandler<in TIntegrationEvent>
    where TIntegrationEvent : IIntegrationEvent
{
    Task Handle(TIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
```

`ICorrelationIdProvider.CorrelationId`: no request HTTP vem do header
`X-Correlation-Id` (ou é gerado); no Worker vem da `CorrelationId` da mensagem.

## OutboxMessage

```csharp
using System.Text.Json;
using CashFlow.Common.Application.EventBus;

namespace CashFlow.Common.Application.Outbox;

/// <summary>
/// Uma integration event que ainda não chegou ao broker. Gravada pelo handler no
/// mesmo SaveChangesAsync do fato que ela descreve; marcada como publicada
/// somente depois do confirm do RabbitMQ.
/// </summary>
/// <remarks>
/// O Id é o próprio Id do evento, não um id de entrega. Uma publicação pode
/// acontecer mais de uma vez (confirm perdido, retry), e o consumidor deduplica
/// por esse id na Inbox. Por isso ele nasce no evento e é copiado para cá.
/// </remarks>
public sealed class OutboxMessage
{
    public static readonly JsonSerializerOptions PayloadSerializerOptions = CreateOptions();

    private OutboxMessage()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>Literal versionado do contrato, ex.: "transactions.transaction-created.v1".</summary>
    public string Type { get; private init; } = string.Empty;

    public string Payload { get; private init; } = string.Empty;

    public string CorrelationId { get; private init; } = string.Empty;

    public DateTime OccurredOnUtc { get; private init; }

    public DateTime? PublishedOnUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage From<TEvent>(TEvent integrationEvent, string type, string correlationId)
        where TEvent : IIntegrationEvent =>
        new()
        {
            Id = integrationEvent.Id,
            Type = type,
            Payload = JsonSerializer.Serialize(integrationEvent, PayloadSerializerOptions),
            CorrelationId = correlationId,
            OccurredOnUtc = integrationEvent.OccurredOnUtc
        };

    public void MarkPublished(DateTime nowUtc)
    {
        PublishedOnUtc = nowUtc;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        AttemptCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
```

Configuração EF em `Common.Infrastructure/Outbox/OutboxMessageConfiguration.cs`
(tabela `outbox_messages`, `payload` como `jsonb`, `type` até 200, `correlation_id`
até 100, índice parcial `WHERE published_on_utc IS NULL` em `occurred_on_utc`).
O DbContext do módulo que publica chama `modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration())`.

## Common.Infrastructure

- `Time/DateTimeProvider.cs`: `internal sealed`, `UtcNow => DateTime.UtcNow`.
- `Correlation/CorrelationIdProvider.cs` + `CorrelationIdMiddleware` (na Presentation):
  lê `X-Correlation-Id`, aceita no máximo 100 caracteres `[A-Za-z0-9-]`, senão gera um GUID,
  e devolve o header na resposta.
- `Messaging/`: ver [messaging.md](messaging.md).
- `InfrastructureConfiguration.AddCommonInfrastructure(IServiceCollection)`: relógio e
  correlation id. **Não** registra RabbitMQ: só o Worker fala com o broker
  (`AddMessaging(IConfiguration)`), e isso é parte do motivo de a API não cair quando
  o broker cai.

## Common.Presentation

`IEndpoint`, `EndpointExtensions.AddEndpoints(params Assembly[])` e
`MapEndpoints(this WebApplication)` copiados do CoreMastery; `ApiResults.Problem(Result)`
é o único lugar que traduz `ErrorType` em status HTTP (400/404/409/500), com a
extensão `code` no ProblemDetails. `UnhandledExceptionHandler` devolve 500 sem
detalhe interno.
