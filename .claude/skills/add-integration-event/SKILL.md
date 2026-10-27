---
name: add-integration-event
description: 'Cria ou altera a comunicação entre módulos do CashFlow - contrato versionado no projeto IntegrationEvents, escrita na Outbox na mesma transação, publisher em background com publisher confirms e Polly, consumidor RabbitMQ com Inbox e atualização atômica, ACK só após commit. Use quando o usuário pedir evento, mensagem, Outbox, Inbox, publisher, consumer, RabbitMQ, TransactionCreated, ou a consolidação do saldo diário.'
argument-hint: '<evento, ex.: "TransactionCreated.v1">'
---

# Adicionar uma integration event (Outbox → RabbitMQ → Inbox)

Este é o coração do desafio, e o lugar onde o erro é silencioso: tudo compila,
os testes do caminho feliz passam, e um redelivery dobra o saldo. Trate cada
regra abaixo como inegociável.

Pré-requisitos: `scaffold-solution` (base de mensageria em `Common.Infrastructure`)
e os dois módulos criados. Base genérica descrita em
[../scaffold-solution/references/messaging.md](../scaffold-solution/references/messaging.md).

## As quatro peças e quem é dono de cada uma

| Peça | Onde | Dono |
|---|---|---|
| Contrato `TransactionCreatedIntegrationEvent` (v1) | `CashFlow.Modules.Transactions.IntegrationEvents` | Transactions |
| Escrita na Outbox | handler do command, mesmo `SaveChangesAsync` | Transactions |
| `OutboxPublisher` (BackgroundService) | `Transactions.Infrastructure/Outbox/` | Transactions, roda no Worker |
| Consumer + Inbox + saldo | `Consolidation.Presentation` (handler) → `Consolidation.Application` (command) → `Consolidation.Infrastructure` (SQL) | Consolidation, roda no Worker |

## 1. O contrato

```csharp
namespace CashFlow.Modules.Transactions.IntegrationEvents;

/// <summary>
/// Um lançamento foi registrado. Contrato público e versionado: um campo novo
/// opcional é compatível; remover, renomear ou mudar o significado de um campo
/// exige TransactionCreated.v2, publicado ao lado do v1 até ninguém mais consumir o v1.
/// </summary>
public sealed record TransactionCreatedIntegrationEvent(
    Guid Id,                 // eventId: chave de deduplicação na Inbox
    DateTime OccurredOnUtc,
    Guid TransactionId,
    string Type,             // "Credit" | "Debit"
    decimal Amount,
    DateOnly BusinessDate,
    DateTimeOffset OccurredAt,
    int Version,
    string CorrelationId) : IntegrationEvent(Id, OccurredOnUtc)
{
    public const string MessageType = "transactions.transaction-created.v1";
    public const string Exchange = "cashflow.transactions";
    public const string RoutingKey = "transaction-created.v1";
}
```

Ids e valores; nada de tipo de domínio, nada de descrição livre (evita dado
pessoal desnecessário). O `From(transaction, …)` que monta o evento fica na
Application de Transactions, não no contrato — o contrato não conhece o Domain.

## 2. Escrita na Outbox

No handler, `context.Transactions.Add(...)` e `context.OutboxMessages.Add(OutboxMessage.From(...))`
antes de **um** `SaveChangesAsync`. Ver [../add-feature/references/command-slice.md](../add-feature/references/command-slice.md).
Nenhuma chamada ao broker no request.

## 3. Publisher da Outbox (Transactions, no Worker)

`internal sealed class OutboxPublisher : BackgroundService`, registrado em
`AddTransactionsWorker`. Um ciclo:

1. Abre escopo, `TransactionsDbContext`, `BeginTransactionAsync`.
2. Seleciona um lote com bloqueio de linha, para que duas instâncias de Worker
   não peguem a mesma mensagem:

   ```sql
   SELECT * FROM transactions.outbox_messages
   WHERE published_on_utc IS NULL
   ORDER BY occurred_on_utc
   LIMIT {batchSize}
   FOR UPDATE SKIP LOCKED
   ```

   (`FromSql` com o schema do próprio módulo; `batchSize` é inteiro de config, não texto do usuário.)
3. Para cada mensagem, em ordem: `IRabbitMqPublisher.PublishAsync` (que só volta
   depois do confirm) → `message.MarkPublished(now)`.
   Na primeira falha: `message.RecordFailure(error)`, para o lote (preserva ordem).
4. `SaveChangesAsync` + `CommitAsync`. **`published_on_utc` só existe para
   mensagens que o broker confirmou.**
5. Lote vazio → espera `PollingInterval` (padrão 1 s). Circuito aberto
   (`BrokenCircuitException`) → espera e tenta no próximo ciclo, sem derrubar o host.

Garantia resultante: **at-least-once**. Se o Worker morrer entre o confirm e o
commit, a mensagem é publicada de novo — e a Inbox absorve.

Declaração do exchange (`topic`, durável) no startup do publisher.

## 4. Consumer + Inbox (Consolidation, no Worker)

**Presentation** — a porta de entrada, como um endpoint:

```csharp
internal sealed class TransactionCreatedIntegrationEventHandler(
    ICommandHandler<ApplyTransactionToDailyBalanceCommand> handler)
    : IIntegrationEventHandler<TransactionCreatedIntegrationEvent>
{
    public async Task Handle(TransactionCreatedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        Result result = await handler.Handle(new ApplyTransactionToDailyBalanceCommand(
            e.Id, e.BusinessDate, e.Type, e.Amount), cancellationToken);

        // Diferente do CoreMastery (bus em processo, só loga): aqui a falha precisa
        // voltar ao broker. Exceção = NACK = reentrega. Retornar = ACK.
        if (result.IsFailure)
        {
            throw new IntegrationEventHandlingException(e.Id, result.Error);
        }
    }
}
```

**Application** — `ApplyTransactionToDailyBalanceCommand(Guid EventId, DateOnly BusinessDate, string Type, decimal Amount)`,
com validator (eventId não vazio, tipo válido, valor > 0). O handler chama a
porta `IDailyBalanceProjection.ApplyAsync(...)` e devolve `Result.Success()`
também quando o evento é duplicado (duplicado não é erro: é o caso que a Inbox existe para cobrir).

**Infrastructure** — a implementação é **uma instrução SQL**, atômica por construção:

```sql
WITH inbox AS (
    INSERT INTO consolidation.inbox_messages (event_id, message_type, processed_at)
    VALUES (@eventId, @messageType, @now)
    ON CONFLICT (event_id) DO NOTHING
    RETURNING event_id
)
INSERT INTO consolidation.daily_balances (business_date, credits, debits, updated_at)
SELECT @businessDate, @credit, @debit, @now
FROM inbox
ON CONFLICT (business_date) DO UPDATE
SET credits    = daily_balances.credits + EXCLUDED.credits,
    debits     = daily_balances.debits  + EXCLUDED.debits,
    updated_at = EXCLUDED.updated_at;
```

Com `@credit = Amount, @debit = 0` para crédito e o inverso para débito. Execute
com `Database.ExecuteSqlInterpolatedAsync` (parametrizado — **nunca** concatenação).
Retorno 0 linhas = evento já processado. Por que assim:

- Inbox e saldo na mesma instrução → mesma transação. Não existe "registrou na
  Inbox e não somou" nem "somou e não registrou".
- Duas entregas simultâneas do mesmo evento: a segunda espera o índice único da
  primeira e cai no `DO NOTHING`. Nada é somado duas vezes.
- O incremento é `credits = credits + x` no banco, não leia-some-grave na
  aplicação: dois eventos diferentes do mesmo dia em paralelo não se perdem.

**Não** use o padrão do evently (`SELECT EXISTS` numa conexão, handler, `INSERT`
em outra): entre um e outro cabe um redelivery.

Registro em `AddConsolidationWorker`:
`services.AddIntegrationEventConsumer<TransactionCreatedIntegrationEvent>(…queue "consolidation.transaction-created.v1"…)`.

## 5. Testes obrigatórios (integração, containers reais)

| Teste | Afirma |
|---|---|
| POST grava lançamento + Outbox atomicamente | Uma linha em cada; se o `SaveChanges` falhar, nenhuma |
| Publisher marca só após confirm | Com broker parado: mensagem continua `published_on_utc IS NULL` e `attempt_count` cresce; broker de volta: publicada e marcada |
| Mesmo eventId entregue duas vezes | Saldo igual ao de uma entrega; uma linha na Inbox |
| N eventos concorrentes, mesma data | `credits`/`debits` = soma exata calculada no teste |
| Handler lança | Mensagem não é ACKada e volta para a fila |
| Isolamento por role | `consolidation_app` não grava em `transactions.*` e vice-versa (`42501`) |

## Nunca

- Marcar `published_on_utc` antes do confirm, ou usar `BasicPublish` sem confirms.
- ACK antes do commit, ou `autoAck: true`.
- Retry com Polly no `SaveChanges` do lançamento ou no SQL da Inbox (o retry da
  Inbox é a reentrega do broker, que é segura justamente por causa da Inbox).
- Consolidation lendo `transactions.*` para "conferir" um evento.
- Mudar o significado de um campo do v1.
