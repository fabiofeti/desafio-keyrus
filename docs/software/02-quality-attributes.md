# Software — requisitos não funcionais e resiliência

## Independência operacional
Queda do worker ou RabbitMQ não deve bloquear POST: a API persiste Transactions + Outbox sem chamada síncrona a Consolidation. Queda do PostgreSQL compartilhado afeta a escrita; documentar como trade-off.

## Polly
Aplicar `Microsoft.Extensions.Resilience`/Polly em operações transitórias onde couber: timeout, retry exponencial com jitter, circuit breaker para conexões/publicação do broker. Evitar retry ilimitado e retry de transação não idempotente. No consumidor, falha persistente tem reentrega com backoff limitada por `x-delivery-limit` (fila quorum) e, depois do limite, DLQ; replay automatizado da DLQ é evolução. Um circuit breaker no publisher não interrompe a API.

## Segurança
Validação de input, limites de tamanho, SQL parametrizado/EF Core, credenciais segregadas por módulo, logs sem segredos, HTTPS/TLS em produção. Auditoria de segredos, inventário de dados sensíveis e prática para produção (Key Vault, Managed Identity, rotação): [05-sensitive-data-and-secrets](05-sensitive-data-and-secrets.md). Autenticação JWT/OAuth2 fica como evolução. Rate limiting (por IP e por usuário do Entra ID) e limite de concorrência com semáforo estão planejados, e adiados de propósito até haver métrica de consumo: [04-rate-limiting-and-concurrency](04-rate-limiting-and-concurrency.md).

## Performance
Índice por `business_date` em Transactions, PK por data em Consolidation, consultas de saldo diretamente na tabela agregada. Pool de conexões e paginação em listagem. Testar 50 RPS no endpoint de consolidação; medir latência e taxa de falhas. Não confundir perda de requisição HTTP com perda de mensagem RabbitMQ.

## Disponibilidade e confiabilidade
Fila durável, mensagens persistentes, publisher confirms, Outbox e Inbox; monitorar backlog, idade da Outbox e atrasos de projeção. Métricas propostas: p95/p99, disponibilidade observada, RPS, HTTP 5xx, lag de consolidação, mensagens duplicadas descartadas.

## Observabilidade (implementado)
- **Health:** `/health/live` não testa nada (o processo responde); `/health/ready` abre uma conexão por módulo com a role do próprio módulo (`transactions-database`, `consolidation-database`) e devolve `503` se uma delas falhar. `/health` continua como liveness, usado pelo healthcheck do Compose. O broker fica fora da readiness da API de propósito: a API não depende dele. O Worker não expõe HTTP; a saúde dele aparece nos logs e nas métricas.
- **Logs:** no Compose, console em JSON (uma linha por evento, timestamp UTC) com escopos. A API abre o escopo `CorrelationId` em cada request (também no 400/500 do `UnhandledExceptionHandler`); o Worker abre `CorrelationId`, `MessageId` e `MessageType` em cada mensagem publicada pela Outbox e em cada mensagem consumida. Um filtro por `CorrelationId` segue um POST até a consolidação.
- **Métricas** (`System.Diagnostics.Metrics`, sem exportador; leitura com `dotnet-counters`, prontas para OpenTelemetry):

| Meter | Instrumento | Tipo | Onde |
|---|---|---|---|
| `CashFlow.Transactions` | `cashflow.transactions.created` (tag `type`) | contador | API, após o commit |
| | `cashflow.transactions.replayed` | contador | API, replay por `Idempotency-Key` |
| | `cashflow.outbox.published` / `publish_failures` / `circuit_open` | contadores | Worker, publisher da Outbox |
| `CashFlow.Consolidation` | `cashflow.consolidation.events_applied` / `events_duplicate` | contadores | Worker, Inbox |
| | `cashflow.consolidation.lag` (s) | histograma | Worker: do evento na Outbox ao saldo |
| `CashFlow.Messaging` | `cashflow.messaging.handler_failures` (tag `queue`) | contador | Worker, consumidor |
| | `cashflow.messaging.dead_lettered` (tags `queue`, `reason`: `unreadable`, `delivery_limit`) | contador | Worker, consumidor |

Fora do escopo implementado: profundidade da fila e idade da Outbox como métricas (hoje pela management UI e por SQL), exportação para Prometheus/OTLP, dashboards e alertas (P2).

