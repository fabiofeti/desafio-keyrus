# Plano de execução para Claude Code

## Estado atual (atualizado em 2026-10-26)
Ponto de retomada para uma sessão nova. Leia também [CLAUDE.md](../CLAUDE.md) e [.claude/skills/README.md](../.claude/skills/README.md).

**Pronto e verificado** (evidência em [03-testing](software/03-testing.md#evidência)):
- Convenções do monólito modular em `.claude/` (skills, comandos `/verify` e `/next-item`, hooks de fronteira entre módulos, segredos e verificação ao fim do turno).
- Módulos `Transactions` e `Consolidation` via `add-module`: cinco projetos cada em `src/Modules/{Module}`, `{Module}DbContext` (schema, connection string e role próprias, histórico de migrations no próprio schema), registro na API (`Add{Module}Module`), no Worker (`Add{Module}Worker`), no migrator e nos testes de arquitetura; projetos `CashFlow.Modules.{Module}.UnitTests`.
- Lançamentos (`add-entity` + `add-feature`): agregado `Transaction` imutável, `OutboxMessage` no `TransactionsDbContext`, migration `Add_Transactions_And_Outbox`; `POST /transactions` (Idempotency-Key, replay, 409, corrida resolvida pelo índice único) grava lançamento e `TransactionCreated.v1` na Outbox no mesmo `SaveChangesAsync`; `GET /transactions?date=` paginado. Falhas de binding (data/JSON inválidos) passaram a responder 400 em vez de 500.
- Integração entre módulos (`add-integration-event`): `OutboxProcessor`/`OutboxPublisher` no Worker (`FOR UPDATE SKIP LOCKED`, marca só após confirm, para no primeiro erro); `DailyBalance` + `InboxMessage` e migration `Add_Daily_Balances_And_Inbox`; consumer `consolidation.transaction-created.v1` → `ApplyTransactionToDailyBalanceCommand` → uma instrução SQL atômica (Inbox + incremento). Consolidation referencia só `Transactions.IntegrationEvents`.
- P2 (documentação): evolução por item e procedimento de extração de módulo; escala do Worker medida com 3 réplicas.
- Contratos (P1): snapshot do OpenAPI gerado e exemplo do `TransactionCreated.v1` com testes de divergência (produtor e consumidor); endpoints anotados (limites, headers de resposta, 400 como problem details); diagramas C4 e de sequência com os nomes do código, sintaxe validada.
- Observabilidade (P1, decisão do usuário: `Meter` nativo + testes, sem exportador): `/health/live` e `/health/ready` (PostgreSQL por módulo, broker fora), logs JSON com escopo `CorrelationId` (+ `MessageId`/`MessageType` no Worker), instrumentos `CashFlow.Transactions`/`Consolidation`/`Messaging`; `ThrowOnBadRequest` para o 400 de binding sair em problem details também em Production.
- Queda do broker (`add-challenge`, P1): `tests/chaos/broker-down.sh` para o RabbitMQ, envia N lançamentos, mantém o broker fora por `OUTAGE` s, religa e confere POSTs 201, Outbox retida, breaker aberto, Worker vivo, reconexão de publisher e consumer, soma exata e fila drenada.
- README final: o que foi entregue, arquitetura, portas/URLs, `curl` com saídas reais, testes, falha, carga e limitações conhecidas.
- Carga (`add-challenge`): `tests/load/daily-balances.js` (k6, 50/s por 2 min, critério de perda fixado no script) executado com o Worker ligado e parado; resultados e limites da medição na evidência.
- Teste de falha (`add-challenge`): `tests/chaos/worker-down.sh` para o Worker, envia N lançamentos, religa, confere soma exata, Outbox vazia, Inbox = eventos e redelivery ignorada (management API + log do Worker).
- Cobertura revisada (`add-challenge`): mapa de testes por regra na evidência; novos testes de atomicidade lançamento + Outbox (trigger que recusa a Outbox; validado por mutação) e de escrita cruzada nas tabelas reais.
- Saldo diário (`add-feature`): `GET /daily-balances/{date}` lê só `consolidation.daily_balances` pelo `IConsolidationDbContext`; dia sem lançamento = 200 com zeros e `lastUpdatedAt: null`; `consistency: "eventual"`; data inválida 400.
- DLQ (antecipada do P2 a pedido): fila quorum com `x-delivery-limit` e dead-letter at-least-once para `consolidation.transaction-created.v1.dlq`; consumer usa `basic.reject` com requeue (o `nack` não conta para o limite no RabbitMQ 4).
- Etapa (1) via `scaffold-solution`: `CashFlow.slnx`, `Common.*`, `CashFlow.Hosting/Modules.cs` (lista de módulos), `CashFlow.Api` (endpoints descobertos por scanning; modo `--migrate`), `CashFlow.Worker` (mensageria RabbitMQ com confirms + Polly), testes de arquitetura (15) e de integração (16), Dockerfiles, Compose e `deploy/postgres/init/01-roles.sh`.

**Placar na última sessão:** `dotnet build` 0 erros/0 warnings; `dotnet test CashFlow.slnx` 138/138 (15 arquitetura, 36 Transactions, 22 Consolidation, 65 integração). Compose validado de ponta a ponta, inclusive o `GET /daily-balances`, os testes de falha do Worker e do broker e a carga de 50 RPS, e deixado parado (`docker compose stop`, volumes mantidos).

**Observações em aberto** (registradas, sem ação decidida):
- Imagem do Worker sem `tzdata`; hoje só o POST (API) usa `IBusinessCalendar`. Vira bug se um handler do Worker passar a usar.
- Limites de `description` (200) e valor máximo só no validator, não no agregado `Transaction`.
- Com `DeliveryLimit` 5, uma mensagem envenenada leva ~40 s de backoff até a DLQ e segura a fila nesse intervalo; replay da DLQ é manual.
- Se o publisher rodar antes de o consumer declarar a fila, o broker devolve `NO_ROUTE`; a linha fica na Outbox e sai no ciclo seguinte (`attempt_count` sobe).

**P0 e P1 completos; P2 documentado.** Próximos passos só com pedido do usuário.

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
- [x] Unitários e integração para regras, isolamento e idempotência. *(Revisão da cobertura + duas lacunas fechadas: atomicidade lançamento/Outbox sob falha e escrita cruzada nas tabelas reais — [testes](software/03-testing.md#evidência).)*
- [x] Teste de falha: parar worker, criar lançamentos, reiniciar, verificar convergência sem duplicação. *(Evidência: `tests/chaos/worker-down.sh`, PASS em duas execuções — [testes](software/03-testing.md#evidência).)*
- [x] Executar carga de 50 RPS na leitura de consolidados; medir perda, latência, erros e janela de teste. *(Evidência: k6 2 min a 50/s, Worker ligado e parado, perda 0,00%, p99 < 3 ms localmente — [testes](software/03-testing.md#evidência).)*
- [x] Atualizar README com portas, URLs, curl, comandos e resultados observados. *(Exemplos de `curl` executados no Compose em 2026-10-26 e saídas reais no [README](../README.md); limitações explícitas.)*

## P1 — se houver tempo
- [x] Retry exponencial + jitter e circuit breaker com Polly na publicação e conexões transitórias; evitar circuit breaker no caminho crítico de gravação de Transactions por indisponibilidade de Consolidation. *(Pipelines em `RabbitMqResilience` só no Worker; `Api_Should_NotTalkToTheBroker`; `tests/chaos/broker-down.sh` PASS em duas execuções — [testes](software/03-testing.md#evidência).)*
- [x] Health/readiness, logs estruturados com correlation ID e métricas básicas. *(`/health/live` e `/health/ready`, logs JSON com escopos, `Meter` por módulo lido com `dotnet-counters` — [observabilidade](software/02-quality-attributes.md#observabilidade-implementado), [testes](software/03-testing.md#evidência).)*
- [x] Contratos de API documentados e diagrama atualizado a partir do código. *([openapi.json](api/openapi.json) e [exemplo do evento](api/events/transactions.transaction-created.v1.json) guardados por testes de divergência; [diagramas](diagrams/architecture.md) redesenhados com os nomes do código — [testes](software/03-testing.md#evidência).)*

## P2 — apenas documentar
Escala horizontal, HA do broker/banco, autenticação real, replay operacional da DLQ (a DLQ em si foi antecipada a pedido), dashboards, custos cloud validados, deploy em produção.
- [x] Documentado em [04-evolution-and-module-extraction](solucoes/04-evolution-and-module-extraction.md): proposta por item, gatilhos para extrair um módulo e o procedimento em fases (exemplo: Consolidation). Única execução: 3 réplicas do Worker medidas com `tests/chaos/scale-workers.sh` — PASS em duas execuções, soma exata, nada publicado ou aplicado duas vezes ([testes](software/03-testing.md#evidência)). Nada de código novo.

## Instruções de execução
Não adicionar funcionalidades fora do desafio. Depois de cada P0, rodar `dotnet build`/`dotnet test`; não marcar como concluído sem evidência. Implementar primeiro o caminho feliz end-to-end e então resiliência. Manter a solução simples.
