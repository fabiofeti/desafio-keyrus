# Diagramas — C4 e sequências (Mermaid)

> Mermaid não suporta todos os recursos do C4 igualmente em todos os renderizadores. Diagramas C4Context/C4Container/C4Component abaixo usam a sintaxe C4 do Mermaid; nível 4 usa classDiagram como representação de código. Verificar renderização no GitHub e ajustar se necessário.

## C4 — Nível 1: Contexto
```mermaid
C4Context
  title Cash Flow - Contexto
  Person(merchant, "Comerciante", "Registra e consulta movimentações")
  System(cashflow, "Cash Flow", "Lançamentos e saldo diário")
  Rel(merchant, cashflow, "Registra créditos/débitos e consulta saldos", "HTTPS")
```

## C4 — Nível 2: Containers
```mermaid
C4Container
  title Cash Flow - Containers
  Person(merchant, "Comerciante")
  System_Boundary(system, "Cash Flow") {
    Container(api, "CashFlow.Api", ".NET 10", "Endpoints de Transactions e Consolidation")
    Container(worker, "CashFlow.Worker", ".NET 10", "Outbox publisher e consumidor")
    ContainerDb(db, "PostgreSQL", "Schemas isolados", "transactions e consolidation")
    ContainerQueue(broker, "RabbitMQ", "AMQP", "Eventos persistentes")
  }
  Rel(merchant, api, "HTTP")
  Rel(api, db, "Lê/grava conforme ownership")
  Rel(worker, db, "Lê/grava com roles por módulo")
  Rel(worker, broker, "Publica e consome")
```

## C4 — Nível 3: Componentes
```mermaid
C4Component
  title CashFlow - Componentes lógicos
  Container_Boundary(api, "API") {
    Component(tx, "Transactions endpoints", "Minimal API", "Valida e grava lançamento + Outbox")
    Component(read, "Consolidation query", "Minimal API", "Consulta projeção de saldos")
  }
  Container_Boundary(worker, "Worker") {
    Component(pub, "Outbox publisher", "BackgroundService", "Publica eventos")
    Component(cons, "Consolidation consumer", "BackgroundService", "Deduplica e atualiza saldo")
  }
  ContainerDb(tdb, "transactions schema", "PostgreSQL", "Lançamentos e Outbox")
  ContainerDb(cdb, "consolidation schema", "PostgreSQL", "Saldos e Inbox")
  ContainerQueue(mq, "RabbitMQ", "AMQP", "TransactionCreated.v1")
  Rel(tx, tdb, "INSERT/SELECT")
  Rel(read, cdb, "SELECT")
  Rel(pub, tdb, "SELECT/UPDATE Outbox")
  Rel(pub, mq, "Publish")
  Rel(mq, cons, "Consume")
  Rel(cons, cdb, "UPSERT + Inbox")
```

## C4 — Nível 4: Código (recorte Transactions)
```mermaid
classDiagram
  class TransactionEndpoint { +Create(request) }
  class CreateTransactionHandler { +Handle(command) }
  class Transaction { +Id : Guid; +Type : TransactionType; +Amount : decimal }
  class TransactionsDbContext { +Transactions; +OutboxMessages }
  class OutboxMessage { +EventId : Guid; +Payload : string }
  TransactionEndpoint --> CreateTransactionHandler
  CreateTransactionHandler --> Transaction
  CreateTransactionHandler --> TransactionsDbContext
  TransactionsDbContext --> OutboxMessage
```

## Sequência — registro
```mermaid
sequenceDiagram
  actor User as Comerciante
  participant API as API Transactions
  participant DB as PostgreSQL transactions
  participant PUB as Outbox Publisher
  participant MQ as RabbitMQ
  User->>API: POST /transactions + Idempotency-Key
  API->>DB: BEGIN, INSERT transaction + outbox, COMMIT
  DB-->>API: Commit OK
  API-->>User: 201 Created
  PUB->>DB: Buscar Outbox pendente
  PUB->>MQ: Publish TransactionCreated.v1
  MQ-->>PUB: Publisher confirm
  PUB->>DB: Marcar publicado
```

## Sequência — consolidação e falha
```mermaid
sequenceDiagram
  participant MQ as RabbitMQ
  participant W as Consolidation Consumer
  participant DB as PostgreSQL consolidation
  participant API as API Consolidation
  actor User as Comerciante
  MQ->>W: TransactionCreated.v1
  W->>DB: INSERT Inbox ON CONFLICT DO NOTHING + UPSERT saldo (uma instrução, atômica)
  alt Evento novo
    DB-->>W: 1 linha, saldo incrementado e commitado
  else Duplicado (mesmo eventId)
    DB-->>W: 0 linhas, sem incremento
  end
  W-->>MQ: ACK somente após o commit
  Note over W,MQ: Falha no processamento gera NACK e o broker reentrega
  User->>API: GET /daily-balances/{date}
  API->>DB: SELECT saldo
  DB-->>API: Projeção eventual
  API-->>User: 200 + saldo + lastUpdatedAt
  Note over MQ,W: Se Worker parar, fila retém eventos e Transactions continua gravando
```
