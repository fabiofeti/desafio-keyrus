# Software — design, persistência e contratos

## Estrutura
```text
src/
  Api/CashFlow.Api/                 # host HTTP; endpoints descobertos nos módulos; modo --migrate
  Worker/CashFlow.Worker/           # host de background: outbox publisher + consumer da consolidação
  Hosting/CashFlow.Hosting/         # lista de módulos, compartilhada pelos dois hosts
  Common/CashFlow.Common.*/         # Domain, Application, Infrastructure, Presentation
  Modules/Transactions/             # Domain, Application, Infrastructure, Presentation, IntegrationEvents
  Modules/Consolidation/            # idem
tests/
  CashFlow.ArchitectureTests/
  CashFlow.Modules.{Module}.UnitTests/
  CashFlow.IntegrationTests/
```
O contrato `TransactionCreated.v1` vive em `CashFlow.Modules.Transactions.IntegrationEvents` (papel do antigo `Shared.Contracts`): é o único assembly de um módulo que outro módulo pode referenciar.
API referencia módulos para expor endpoints; Worker referencia os mesmos assemblies para executar handlers, **sem duplicar domínio**. O Worker não deve receber permissão de escrita cruzada por usar ambos: configurar DbContexts com roles específicas e manter handlers separados.

## Contratos HTTP
`POST /transactions` com header `Idempotency-Key` e JSON:
```json
{"type":"Credit","amount":125.50,"occurredAt":"2026-10-26T10:00:00-03:00","description":"Venda"}
```
Retorno `201`, `Location: /transactions/{id}` e corpo `id`, `type`, `amount`, `occurredAt` (normalizado para UTC), `businessDate` (dia no fuso `America/Sao_Paulo`, configurável em `BusinessCalendar:TimeZone`), `description`.
- Mesma chave e mesmo payload: o mesmo corpo, `201`, header `Idempotency-Replayed: true`. O payload é comparado normalizado (valor com duas casas, instante em UTC).
- Mesma chave e payload diferente: `409`, `code: Transactions.IdempotencyKeyReused`.
- Duas requisições simultâneas com a mesma chave: uma grava, a outra é tratada como repetição (índice único + releitura, sem retry).
- Sem chave, chave fora de `[A-Za-z0-9_-]{1,100}`, `type` diferente de `Credit`/`Debit`, `amount` ≤ 0, com mais de 2 casas ou acima de 999.999.999,99, `description` com mais de 200 caracteres, ou JSON inválido: `400` (problem details com `code`).

`GET /transactions?date=2026-10-26&page=1&pageSize=50` retorna `{"items":[…],"page":1,"pageSize":50,"totalCount":N}`, ordenado por `occurredAt` e `id`. `page` ≥ 1, `pageSize` entre 1 e 200 (padrão 50); data ausente ou fora de `YYYY-MM-DD`: `400`. Data sem lançamentos: `200` com `items` vazio.

`GET /daily-balances/2026-10-26` retorna:
```json
{"date":"2026-10-26","credits":125.50,"debits":20.00,"balance":105.50,"lastUpdatedAt":"2026-10-26T13:01:00Z"}
```
Se não houver lançamentos, definir comportamento estável (zero ou 404) e testar. Não prometer leitura sincronizada com POST.

## Evento
`TransactionCreated.v1` (`type` da mensagem `transactions.transaction-created.v1`): `id` (eventId, chave de deduplicação), `occurredOnUtc`, `transactionId`, `type`, `amount`, `businessDate`, `occurredAt`, `version`, `correlationId`. Publicação persistente com confirmação. Evitar dados pessoais desnecessários.

Topologia: exchange `cashflow.transactions` (topic, durável), routing key `transaction-created.v1`, fila durável `consolidation.transaction-created.v1` declarada pelo consumidor. Publicação com `mandatory`: sem fila ligada o broker devolve a mensagem (`NO_ROUTE`), o publisher registra a falha e a linha continua na Outbox para o próximo ciclo.

Publisher (Worker, `OutboxProcessor`): a cada ciclo trava até `Outbox:BatchSize` (50) linhas não publicadas com `FOR UPDATE SKIP LOCKED`, publica em ordem, marca `published_on_utc` só após o confirm e para no primeiro erro (incrementa `attempt_count`, grava `last_error`). Lote vazio ou falha: espera `Outbox:PollingInterval` (1 s). Garantia: at-least-once.

## Persistência
- `transactions.transactions`: `id` uuid (v7), `type`, `amount numeric(18,2)`, `occurred_at`, `business_date` (indexada), `description`, `idempotency_key` (única), `request_fingerprint`, `created_at_utc`.
- `transactions.outbox_messages`: `id` uuid (= eventId), `type`, `payload jsonb`, `correlation_id`, `occurred_on_utc`, `published_on_utc`, `attempt_count`, `last_error`; índice parcial nos não publicados.
- `consolidation.daily_balances`: `business_date date` PK, `credits` e `debits numeric(18,2)`, `updated_at_utc`; saldo = `credits - debits`, calculado na leitura.
- `consolidation.inbox_messages`: `event_id uuid` PK, `message_type`, `processed_at_utc`.
- DbContexts/migrations separados; grants por role (`transactions_app` e `consolidation_app`).
- Sem FK entre schemas, sem DbContext de outro módulo e sem gravação cruzada.

## Consistência e concorrência
No consumidor, registrar Inbox e aplicar UPSERT/incremento atômico no saldo **na mesma transação** — implementado como uma única instrução SQL (`WITH inbox AS (INSERT … ON CONFLICT (event_id) DO NOTHING RETURNING …) INSERT INTO daily_balances … SELECT … FROM inbox ON CONFLICT (business_date) DO UPDATE SET credits = credits + EXCLUDED.credits …`); conflito de eventId não incrementa saldo. ACK após commit; falha do handler = NACK com requeue após backoff (1, 2, 5, 10 s). Sem DLQ no MVP: uma mensagem que sempre falha volta à fila indefinidamente (limitação documentada, P2). Valores `decimal` e datas de negócio explícitas.
