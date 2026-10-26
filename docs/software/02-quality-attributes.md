# Software — requisitos não funcionais e resiliência

## Independência operacional
Queda do worker ou RabbitMQ não deve bloquear POST: a API persiste Transactions + Outbox sem chamada síncrona a Consolidation. Queda do PostgreSQL compartilhado afeta a escrita; documentar como trade-off.

## Polly
Aplicar `Microsoft.Extensions.Resilience`/Polly em operações transitórias onde couber: timeout, retry exponencial com jitter, circuit breaker para conexões/publicação do broker. Evitar retry ilimitado e retry de transação não idempotente. No consumidor, falha persistente tem reentrega com backoff limitada por `x-delivery-limit` (fila quorum) e, depois do limite, DLQ; replay automatizado da DLQ é evolução. Um circuit breaker no publisher não interrompe a API.

## Segurança
Validação de input, limites de tamanho, SQL parametrizado/EF Core, credentials segregadas, logs sem segredos, HTTPS/TLS em produção. Autenticação JWT/OAuth2 e rate limiting avançado podem ficar documentados como evolução, se o tempo for curto.

## Performance
Índice por `business_date` em Transactions, PK por data em Consolidation, consultas de saldo diretamente na tabela agregada. Pool de conexões e paginação em listagem. Testar 50 RPS no endpoint de consolidação; medir latência e taxa de falhas. Não confundir perda de requisição HTTP com perda de mensagem RabbitMQ.

## Disponibilidade e confiabilidade
Fila durável, mensagens persistentes, publisher confirms, Outbox e Inbox; monitorar backlog, idade da Outbox e atrasos de projeção. Métricas propostas: p95/p99, disponibilidade observada, RPS, HTTP 5xx, lag de consolidação, mensagens duplicadas descartadas.
