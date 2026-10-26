# Plano de execução para Claude Code

## Estado atual (atualizado em 2026-10-26)
Ponto de retomada para uma sessão nova. Leia também [CLAUDE.md](../CLAUDE.md) e [.claude/skills/README.md](../.claude/skills/README.md).

**Pronto e verificado** (evidência em [03-testing](software/03-testing.md#evidência)):
- Convenções do monólito modular em `.claude/` (skills, comandos `/verify` e `/next-item`, hooks de fronteira entre módulos, segredos e verificação ao fim do turno).
- Módulos `Transactions` e `Consolidation` via `add-module`: cinco projetos cada em `src/Modules/{Module}`, `{Module}DbContext` (schema, connection string e role próprias, histórico de migrations no próprio schema), registro na API (`Add{Module}Module`), no Worker (`Add{Module}Worker`), no migrator e nos testes de arquitetura; projetos `CashFlow.Modules.{Module}.UnitTests`. Nenhuma referência entre os módulos ainda (a de Consolidation para `Transactions.IntegrationEvents` vem com o evento).
- Lançamentos (`add-entity` + `add-feature`): agregado `Transaction` imutável, `OutboxMessage` no `TransactionsDbContext`, migration `Add_Transactions_And_Outbox`; `POST /transactions` (Idempotency-Key, replay, 409, corrida resolvida pelo índice único) grava lançamento e `TransactionCreated.v1` na Outbox no mesmo `SaveChangesAsync`; `GET /transactions?date=` paginado. Falhas de binding (data/JSON inválidos) passaram a responder 400 em vez de 500.
- Integração entre módulos (`add-integration-event`): `OutboxProcessor`/`OutboxPublisher` no Worker (`FOR UPDATE SKIP LOCKED`, marca só após confirm, para no primeiro erro); `DailyBalance` + `InboxMessage` e migration `Add_Daily_Balances_And_Inbox`; consumer `consolidation.transaction-created.v1` → `ApplyTransactionToDailyBalanceCommand` → uma instrução SQL atômica (Inbox + incremento). Consolidation referencia só `Transactions.IntegrationEvents`.
- Etapa (1) via `scaffold-solution`: `CashFlow.slnx`, `Common.*`, `CashFlow.Hosting/Modules.cs` (lista de módulos), `CashFlow.Api` (endpoints descobertos por scanning; modo `--migrate`), `CashFlow.Worker` (mensageria RabbitMQ com confirms + Polly), testes de arquitetura (15) e de integração (16), Dockerfiles, Compose e `deploy/postgres/init/01-roles.sh`.

**Próximos passos, em ordem:**
1. `/add-feature` para `GET /daily-balances/{date}`.
2. `/add-challenge` para os testes de falha e a carga de 50 RPS; README final.

Cada etapa termina com `dotnet build`/`dotnet test` verdes e evidência registrada antes de marcar `[x]`.

## P0 — indispensável
- [x] Criar solução .NET 10 com `CashFlow.Api`, `CashFlow.Worker`, módulos `Transactions`, `Consolidation`, `Shared.Contracts`, testes. *(`Shared.Contracts` é `CashFlow.Modules.Transactions.IntegrationEvents`; ver [design](software/01-design-and-contracts.md#estrutura) e [testes](software/03-testing.md#evidência).)*
- [x] Dockerfiles e Docker Compose: API, Worker, RabbitMQ management, PostgreSQL 17 Alpine; healthchecks e volumes. *(Evidência: `docker compose up --build -d` com todos os serviços saudáveis — ver [testes](software/03-testing.md#evidência).)*
- [x] Configurar schemas, roles com privilégios mínimos, DbContexts e migrations independentes. O runtime de cada módulo usa role restrita; migrations podem usar credenciais administrativas somente no bootstrap. *(Evidência: `DatabaseRoleTests`, `ModuleDatabaseTests`; migrator aplica as duas migrations — [testes](software/03-testing.md#evidência).)*
- [x] `POST /transactions` aceita idempotency key e cria crédito/débito em transação com Outbox. *(Evidência: `TransactionsTests` e handler tests — [testes](software/03-testing.md#evidência).)*
- [x] `GET /transactions?date=...` lista lançamentos. *(Evidência: `TransactionsTests` — [testes](software/03-testing.md#evidência).)*
- [x] Publisher de Outbox envia `TransactionCreated.v1` ao RabbitMQ com publisher confirms; falha não perde registro persistido. *(Evidência: `ConsolidationFlowTests.Publisher_Should_MarkPublishedOnlyAfterConfirm…` — [testes](software/03-testing.md#evidência).)*
- [x] Worker consome evento, deduplica via Inbox e aplica incremento atômico no saldo do dia; ACK apenas após commit. *(Evidência: `ConsolidationFlowTests` — redelivery, concorrência, NACK — [testes](software/03-testing.md#evidência).)*
- [ ] `GET /daily-balances/{date}` devolve créditos, débitos e saldo (com indicação de atualização eventual).
- [ ] Unitários e integração para regras, isolamento e idempotência.
- [ ] Teste de falha: parar worker, criar lançamentos, reiniciar, verificar convergência sem duplicação.
- [ ] Executar carga de 50 RPS na leitura de consolidados; medir perda, latência, erros e janela de teste.
- [ ] Atualizar README com portas, URLs, curl, comandos e resultados observados.

## P1 — se houver tempo
- [ ] Retry exponencial + jitter e circuit breaker com Polly na publicação e conexões transitórias; evitar circuit breaker no caminho crítico de gravação de Transactions por indisponibilidade de Consolidation.
- [ ] Health/readiness, logs estruturados com correlation ID e métricas básicas.
- [ ] Contratos de API documentados e diagrama atualizado a partir do código.

## P2 — apenas documentar
Escala horizontal, HA do broker/banco, autenticação real, DLQ e replay operacional, dashboards, custos cloud validados, deploy em produção.

## Instruções de execução
Não adicionar funcionalidades fora do desafio. Depois de cada P0, rodar `dotnet build`/`dotnet test`; não marcar como concluído sem evidência. Implementar primeiro o caminho feliz end-to-end e então resiliência. Manter a solução simples.
