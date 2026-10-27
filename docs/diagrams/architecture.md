# Diagramas — C4 e sequências (Mermaid)

> Desenhados a partir do código (nomes de projetos, classes, filas e serviços do Compose conferidos em 2026-10-26). Os níveis 1–3 usam a sintaxe C4 do Mermaid; o nível 4 usa `classDiagram`. Sintaxe validada por renderização Mermaid. Contratos: [OpenAPI](../api/openapi.json) e [exemplo do evento](../api/events/transactions.transaction-created.v1.json).

## C4 — Nível 1: Contexto
```mermaid
C4Context
  title Cash Flow - Contexto
  Person(merchant, "Comerciante", "Registra e consulta movimentações")
  System(cashflow, "Cash Flow", "Lançamentos e saldo diário consolidado")
  Rel(merchant, cashflow, "Registra créditos/débitos e consulta saldos", "HTTP/JSON")
```

## C4 — Nível 2: Containers
Serviços do [docker-compose.yml](../../docker-compose.yml). A API não tem dependência do RabbitMQ: é isso que mantém o `POST /transactions` no ar com o broker ou o Worker fora.

```mermaid
C4Container
  title Cash Flow - Containers
  Person(merchant, "Comerciante")
  System_Boundary(system, "Cash Flow") {
    Container(api, "api", "CashFlow.Api, .NET 10", "Minimal APIs dos dois módulos; /health/live, /health/ready")
    Container(worker, "worker", "CashFlow.Worker, .NET 10", "Publisher da Outbox e consumidor da consolidação")
    Container(migrator, "migrator", "CashFlow.Api --migrate", "Aplica as migrations de cada módulo e termina")
    ContainerDb(db, "postgres", "PostgreSQL 17", "Schemas transactions e consolidation; roles transactions_app e consolidation_app")
    ContainerQueue(broker, "rabbitmq", "RabbitMQ 4", "Exchange cashflow.transactions; fila quorum e DLQ")
  }
  Rel(merchant, api, "POST/GET", "HTTP/JSON")
  Rel(api, db, "Cada módulo com a própria role")
  Rel(worker, db, "Outbox e Inbox, cada módulo com a própria role")
  Rel(worker, broker, "Publica com confirm; consome com ACK manual", "AMQP")
  Rel(migrator, db, "DDL com a credencial administrativa")
```

## C4 — Nível 3: Componentes
Cada caixa é uma classe real; os módulos só se falam pelo contrato `TransactionCreated.v1` (projeto `Transactions.IntegrationEvents`).

```mermaid
C4Component
  title CashFlow - Componentes
  Container_Boundary(api, "api") {
    Component(create, "Transactions/Create", "IEndpoint", "POST /transactions")
    Component(list, "Transactions/List", "IEndpoint", "GET /transactions")
    Component(createHandler, "CreateTransactionCommandHandler", "ICommandHandler", "Idempotência; lançamento + Outbox num SaveChanges")
    Component(get, "DailyBalances/Get", "IEndpoint", "GET /daily-balances/{date}")
    Component(getHandler, "GetDailyBalanceQueryHandler", "IQueryHandler", "Lê só daily_balances")
  }
  Container_Boundary(worker, "worker") {
    Component(outbox, "OutboxPublisher + OutboxProcessor", "BackgroundService", "FOR UPDATE SKIP LOCKED; marca após o confirm")
    Component(publisher, "RabbitMqPublisher", "Polly", "Retry com jitter, circuit breaker, timeout")
    Component(consumer, "RabbitMqConsumer", "BackgroundService", "ACK após commit; reject com requeue e backoff")
    Component(apply, "ApplyTransactionToDailyBalanceCommandHandler", "ICommandHandler", "Via TransactionCreatedIntegrationEventHandler")
    Component(projection, "DailyBalanceProjection", "SQL", "Inbox + incremento numa instrução")
  }
  ContainerDb(tdb, "schema transactions", "PostgreSQL", "transactions, outbox_messages")
  ContainerDb(cdb, "schema consolidation", "PostgreSQL", "daily_balances, inbox_messages")
  ContainerQueue(mq, "consolidation.transaction-created.v1", "quorum", "DLQ: .dlq")
  Rel(create, createHandler, "command")
  Rel(createHandler, tdb, "INSERT")
  Rel(list, tdb, "SELECT")
  Rel(get, getHandler, "query")
  Rel(getHandler, cdb, "SELECT")
  Rel(outbox, tdb, "SELECT/UPDATE")
  Rel(outbox, publisher, "publish")
  Rel(publisher, mq, "TransactionCreated.v1")
  Rel(mq, consumer, "deliver")
  Rel(consumer, apply, "handle")
  Rel(apply, projection, "ApplyAsync")
  Rel(projection, cdb, "INSERT ... ON CONFLICT")
```

## C4 — Nível 4: Código (recorte do registro)
```mermaid
classDiagram
  class Create {
    <<IEndpoint>>
    +MapEndpoint(app)
  }
  class CreateTransactionCommandHandler {
    +Handle(CreateTransactionCommand) Result~CreatedTransaction~
    -TryReplayAsync(command, fingerprint)
  }
  class Transaction {
    +Guid Id
    +TransactionType Type
    +decimal Amount
    +DateTimeOffset OccurredAt
    +DateOnly BusinessDate
    +string IdempotencyKey
    +string RequestFingerprint
    +Create(...)$ Result~Transaction~
  }
  class OutboxMessage {
    +Guid Id
    +string Type
    +string Payload
    +string CorrelationId
    +DateTime? PublishedOnUtc
    +int AttemptCount
    +From(event, type, correlationId)$
    +MarkPublished(nowUtc)
    +RecordFailure(error)
  }
  class TransactionCreatedIntegrationEvent {
    <<contrato v1>>
    +Guid Id
    +Guid TransactionId
    +string Type
    +decimal Amount
    +DateOnly BusinessDate
    +int Version
  }
  class TransactionsDbContext {
    +DbSet~Transaction~ Transactions
    +DbSet~OutboxMessage~ OutboxMessages
  }
  Create --> CreateTransactionCommandHandler : ICommandHandler
  CreateTransactionCommandHandler --> Transaction : cria
  CreateTransactionCommandHandler --> TransactionsDbContext : um SaveChangesAsync
  CreateTransactionCommandHandler ..> TransactionCreatedIntegrationEvent : serializa
  TransactionsDbContext --> OutboxMessage
  OutboxMessage ..> TransactionCreatedIntegrationEvent : Payload
```

## Sequência — registro e publicação
```mermaid
sequenceDiagram
  actor User as Comerciante
  participant API as api (Transactions)
  participant DB as PostgreSQL (schema transactions)
  participant PUB as worker: OutboxProcessor
  participant MQ as RabbitMQ
  User->>API: POST /transactions + Idempotency-Key
  API->>DB: chave já existe?
  alt mesma chave e mesmo payload
    API-->>User: 201 + Idempotency-Replayed: true
  else mesma chave e outro payload
    API-->>User: 409 Transactions.IdempotencyKeyReused
  else chave nova
    API->>DB: INSERT transactions + outbox_messages (um commit)
    API-->>User: 201 Created + Location
  end
  loop a cada Outbox:PollingInterval (1 s)
    PUB->>DB: até 50 pendentes, FOR UPDATE SKIP LOCKED
    PUB->>MQ: publish persistente, mandatory
    alt confirm do broker
      MQ-->>PUB: ack
      PUB->>DB: published_on_utc = now, COMMIT
    else falha, timeout ou circuito aberto
      PUB->>DB: attempt_count + 1, last_error, COMMIT
      Note over PUB,DB: para no primeiro erro, a linha continua pendente
    end
  end
```

## Sequência — consolidação, falha e leitura
```mermaid
sequenceDiagram
  participant MQ as RabbitMQ (quorum)
  participant W as worker: RabbitMqConsumer
  participant DB as PostgreSQL (schema consolidation)
  participant API as api (Consolidation)
  actor User as Comerciante
  MQ->>W: TransactionCreated.v1
  W->>DB: WITH inbox AS (INSERT ... ON CONFLICT DO NOTHING) + UPSERT do saldo
  alt evento novo
    DB-->>W: Inbox gravada e saldo incrementado (um commit)
    W-->>MQ: basic.ack
  else duplicado (mesmo eventId)
    DB-->>W: nada alterado
    W-->>MQ: basic.ack
  else falha do handler
    W-->>MQ: basic.reject requeue após backoff de 1, 2, 5, 10 s
    Note over MQ: depois de x-delivery-limit vai para consolidation.transaction-created.v1.dlq
  end
  User->>API: GET /daily-balances/{date}
  API->>DB: SELECT em daily_balances
  API-->>User: 200 com credits, debits, balance, lastUpdatedAt e consistency eventual
  Note over MQ,W: Worker ou broker fora - a fila e a Outbox retêm os eventos e o POST continua 201
```
