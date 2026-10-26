# Plano de execução para Claude Code

## Estado atual (atualizado em 2026-10-26)
Ponto de retomada para uma sessão nova. Leia também [CLAUDE.md](../CLAUDE.md) e [.claude/skills/README.md](../.claude/skills/README.md).

**Pronto e verificado** (evidência em [03-testing](software/03-testing.md#evidência)):
- Convenções do monólito modular em `.claude/` (skills, comandos `/verify` e `/next-item`, hooks de fronteira entre módulos, segredos e verificação ao fim do turno).
- Módulos `Transactions` e `Consolidation` via `add-module`: cinco projetos cada em `src/Modules/{Module}`, `{Module}DbContext` (schema, connection string e role próprias, histórico de migrations no próprio schema), registro na API (`Add{Module}Module`), no Worker (`Add{Module}Worker`), no migrator e nos testes de arquitetura; projetos `CashFlow.Modules.{Module}.UnitTests`.
- Lançamentos (`add-entity` + `add-feature`): agregado `Transaction` imutável, `OutboxMessage` no `TransactionsDbContext`, migration `Add_Transactions_And_Outbox`; `POST /transactions` (Idempotency-Key, replay, 409, corrida resolvida pelo índice único) grava lançamento e `TransactionCreated.v1` na Outbox no mesmo `SaveChangesAsync`; `GET /transactions?date=` paginado. Falhas de binding (data/JSON inválidos) passaram a responder 400 em vez de 500.
- Integração entre módulos (`add-integration-event`): `OutboxProcessor`/`OutboxPublisher` no Worker (`FOR UPDATE SKIP LOCKED`, marca só após confirm, para no primeiro erro); `DailyBalance` + `InboxMessage` e migration `Add_Daily_Balances_And_Inbox`; consumer `consolidation.transaction-created.v1` → `ApplyTransactionToDailyBalanceCommand` → uma instrução SQL atômica (Inbox + incremento). Consolidation referencia só `Transactions.IntegrationEvents`.
- Saldo diário (`add-feature`): `GET /daily-balances/{date}` lê só `consolidation.daily_balances` pelo `IConsolidationDbContext`; dia sem lançamento = 200 com zeros e `lastUpdatedAt: null`; `consistency: "eventual"`; data inválida 400.
- DLQ (antecipada do P2 a pedido): fila quorum com `x-delivery-limit` e dead-letter at-least-once para `consolidation.transaction-created.v1.dlq`; consumer usa `basic.reject` com requeue (o `nack` não conta para o limite no RabbitMQ 4).
- Etapa (1) via `scaffold-solution`: `CashFlow.slnx`, `Common.*`, `CashFlow.Hosting/Modules.cs` (lista de módulos), `CashFlow.Api` (endpoints descobertos por scanning; modo `--migrate`), `CashFlow.Worker` (mensageria RabbitMQ com confirms + Polly), testes de arquitetura (15) e de integração (16), Dockerfiles, Compose e `deploy/postgres/init/01-roles.sh`.

**Placar na última sessão:** `dotnet build` 0 erros/0 warnings; `dotnet test CashFlow.slnx` 120/120 (15 arquitetura, 34 Transactions, 19 Consolidation, 52 integração). Compose validado de ponta a ponta, inclusive o `GET /daily-balances`, e deixado parado (`docker compose stop`, volumes mantidos).

**Observações em aberto** (registradas, sem ação decidida):
- Imagem do Worker sem `tzdata`; hoje só o POST (API) usa `IBusinessCalendar`. Vira bug se um handler do Worker passar a usar.
- Limites de `description` (200) e valor máximo só no validator, não no agregado `Transaction`.
- Com `DeliveryLimit` 5, uma mensagem envenenada leva ~40 s de backoff até a DLQ e segura a fila nesse intervalo; replay da DLQ é manual.
- Se o publisher rodar antes de o consumer declarar a fila, o broker devolve `NO_ROUTE`; a linha fica na Outbox e sai no ciclo seguinte (`attempt_count` sobe).

**Próximos passos, em ordem:**
1. `/add-challenge` para o item "Unitários e integração para regras, isolamento e idempotência" (revisar cobertura existente e marcar com evidência, ou completar lacunas).
2. `/add-challenge` para o teste de falha (parar Worker, criar lançamentos, reiniciar, convergir sem duplicação).
3. `/add-challenge` para a carga de 50 RPS em `GET /daily-balances/{date}`.
4. README final com portas, URLs, curl, comandos e resultados observados.

Cada etapa termina com `dotnet build`/`dotnet test` verdes e evidência registrada antes de marcar `[x]`.

## P0 — indispensável
- [x] Criar solução .NET 10 com `CashFlow.Api`, `CashFlow.Worker`, módulos `Transactions`, `Consolidation`, `Shared.Contracts`, testes. *(`Shared.Contracts` é `CashFlow.Modules.Transactions.IntegrationEvents`; ver [design](software/01-design-and-contracts.md#estrutura) e [testes](software/03-testing.md#evidência).)*
- [x] Dockerfiles e Docker Compose: API, Worker, RabbitMQ management, PostgreSQL 17 Alpine; healthchecks e volumes. *(Evidência: `docker compose up --build -d` com todos os serviços saudáveis — ver [testes](software/03-testing.md#evidência).)*
- [x] Configurar schemas, roles com privilégios mínimos, DbContexts e migrations independentes. O runtime de cada módulo usa role restrita; migrations podem usar credenciais administrativas somente no bootstrap. *(Evidência: `DatabaseRoleTests`, `ModuleDatabaseTests`; migrator aplica as duas migrations — [testes](software/03-testing.md#evidência).)*
- [x] `POST /transactions` aceita idempotency key e cria crédito/débito em transação com Outbox. *(Evidência: `TransactionsTests` e handler tests — [testes](software/03-testing.md#evidência).)*
- [x] `GET /transactions?date=...` lista lançamentos. *(Evidência: `TransactionsTests` — [testes](software/03-testing.md#evidência).)*
- [x] Publisher de Outbox envia `TransactionCreated.v1` ao RabbitMQ com publisher confirms; falha não perde registro persistido. *(Evidência: `ConsolidationFlowTests.Publisher_Should_MarkPublishedOnlyAfterConfirm…` — [testes](software/03-testing.md#evidência).)*
- [x] Worker consome evento, deduplica via Inbox e aplica incremento atômico no saldo do dia; ACK apenas após commit. *(Evidência: `ConsolidationFlowTests` — redelivery, concorrência, NACK — [testes](software/03-testing.md#evidência).)*
- [x] `GET /daily-balances/{date}` devolve créditos, débitos e saldo (com indicação de atualização eventual). *(Evidência: `GetDailyBalanceQueryHandlerTests`, `DailyBalancesTests` e smoke no Compose — [testes](software/03-testing.md#evidência).)*
- [ ] Unitários e integração para regras, isolamento e idempotência.
- [ ] Teste de falha: parar worker, criar lançamentos, reiniciar, verificar convergência sem duplicação.
- [ ] Executar carga de 50 RPS na leitura de consolidados; medir perda, latência, erros e janela de teste.
- [ ] Atualizar README com portas, URLs, curl, comandos e resultados observados.

## P1 — se houver tempo
- [ ] Retry exponencial + jitter e circuit breaker com Polly na publicação e conexões transitórias; evitar circuit breaker no caminho crítico de gravação de Transactions por indisponibilidade de Consolidation.
- [ ] Health/readiness, logs estruturados com correlation ID e métricas básicas.
- [ ] Contratos de API documentados e diagrama atualizado a partir do código.

## P2 — apenas documentar
Escala horizontal, HA do broker/banco, autenticação real, replay operacional da DLQ (a DLQ em si foi antecipada a pedido), dashboards, custos cloud validados, deploy em produção.

## Instruções de execução
Não adicionar funcionalidades fora do desafio. Depois de cada P0, rodar `dotnet build`/`dotnet test`; não marcar como concluído sem evidência. Implementar primeiro o caminho feliz end-to-end e então resiliência. Manter a solução simples.
