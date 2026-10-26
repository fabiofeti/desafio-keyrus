# Software — design, persistência e contratos

## Estrutura sugerida
```text
src/
  CashFlow.Api/               # HTTP endpoints + DI
  CashFlow.Worker/            # outbox publisher + consolidation consumer
  Modules/Transactions/       # domain, application, infrastructure
  Modules/Consolidation/      # domain, application, infrastructure
  Shared.Contracts/           # TransactionCreated.v1
 tests/
  CashFlow.UnitTests/
  CashFlow.IntegrationTests/
```
API referencia módulos para expor endpoints; Worker referencia os mesmos assemblies para executar handlers, **sem duplicar domínio**. O Worker não deve receber permissão de escrita cruzada por usar ambos: configurar DbContexts com roles específicas e manter handlers separados.

## Contratos HTTP (proposta)
`POST /transactions` com header `Idempotency-Key` e JSON:
```json
{"type":"Credit","amount":125.50,"occurredAt":"2026-10-26T10:00:00-03:00","description":"Venda"}
```
Retorno 201 com `id`, `type`, `amount`, `occurredAt`; repetição da mesma chave retorna resultado estável; chave com payload diferente retorna 409.

`GET /transactions?date=2026-10-26` retorna coleção paginável de lançamentos.

`GET /daily-balances/2026-10-26` retorna:
```json
{"date":"2026-10-26","credits":125.50,"debits":20.00,"balance":105.50,"lastUpdatedAt":"2026-10-26T13:01:00Z"}
```
Se não houver lançamentos, definir comportamento estável (zero ou 404) e testar. Não prometer leitura sincronizada com POST.

## Evento
`TransactionCreated.v1`: `eventId`, `transactionId`, `type`, `amount`, `businessDate`, `occurredAt`, `version`, `correlationId`. Publicação persistente com confirmação. Evitar dados pessoais desnecessários.

## Persistência
- `transactions.transactions`: PK UUID, tipo, valor decimal, data, idempotency_key única, created_at.
- `transactions.outbox_messages`: event_id único, payload, created_at, published_at, attempt_count.
- `consolidation.daily_balances`: business_date PK, credits, debits, updated_at.
- `consolidation.inbox_messages`: event_id PK, processed_at.
- DbContexts/migrations separados; grants por role (`transactions_app` e `consolidation_app`).
- Sem FK entre schemas, sem DbContext de outro módulo e sem gravação cruzada.

## Consistência e concorrência
No consumidor, registrar Inbox e aplicar UPSERT/incremento atômico no saldo **na mesma transação**; conflito de eventId não incrementa saldo. ACK após commit, NACK/retry controlado em falhas. Valores `decimal` e datas de negócio explícitas.
