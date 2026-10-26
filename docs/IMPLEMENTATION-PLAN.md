# Plano de execução para Claude Code

## Estado atual (atualizado em 2026-10-26)
Ponto de retomada para uma sessão nova. Leia também [CLAUDE.md](../CLAUDE.md) e [.claude/skills/README.md](../.claude/skills/README.md).

**Pronto e verificado** (evidência em [03-testing](software/03-testing.md#evidência)):
- Convenções do monólito modular em `.claude/` (skills, comandos `/verify` e `/next-item`, hooks de fronteira entre módulos, segredos e verificação ao fim do turno).
- Módulos `Transactions` e `Consolidation` via `add-module`: cinco projetos cada em `src/Modules/{Module}`, `{Module}DbContext` (schema, connection string e role próprias, histórico de migrations no próprio schema), registro na API (`Add{Module}Module`), no Worker (`Add{Module}Worker`), no migrator e nos testes de arquitetura; projetos `CashFlow.Modules.{Module}.UnitTests`. Nenhuma referência entre os módulos ainda (a de Consolidation para `Transactions.IntegrationEvents` vem com o evento).
- Etapa (1) via `scaffold-solution`: `CashFlow.slnx`, `Common.*`, `CashFlow.Hosting/Modules.cs` (lista de módulos, hoje vazia), `CashFlow.Api` (endpoints descobertos por scanning; modo `--migrate`), `CashFlow.Worker` (mensageria RabbitMQ com confirms + Polly), testes de arquitetura (15) e de integração (16), Dockerfiles, Compose e `deploy/postgres/init/01-roles.sh`.

**Parcial:**
- Item 1 de P0: solução e os dois módulos criados. O papel de `Shared.Contracts` é cumprido por `CashFlow.Modules.Transactions.IntegrationEvents` (padrão do monólito modular); falta atualizar o [doc de design](software/01-design-and-contracts.md) quando o primeiro contrato (`TransactionCreated.v1`) existir, e então marcar o item.
- Item 3 de P0: schemas e roles prontos e testados; `TransactionsDbContext` e `ConsolidationDbContext` prontos, ainda sem migrations (vêm com os primeiros agregados).

**Próximos passos, em ordem:**
1. `/add-entity Transaction` + `OutboxMessage` no `TransactionsDbContext` (é aqui que o contexto passa a aplicar `OutboxMessageConfiguration` e ganha a primeira migration: aplicá-la antes, sem migration, faz o `MigrateAsync` falhar por mudanças pendentes no modelo); `/add-feature` para `POST /transactions` (idempotency key + Outbox) e `GET /transactions?date=`.
2. `/add-integration-event TransactionCreated.v1`: publisher da Outbox, consumer com Inbox atômica, `DailyBalance`.
3. `/add-feature` para `GET /daily-balances/{date}`.
4. `/add-challenge` para os testes de falha e a carga de 50 RPS; README final.

Cada etapa termina com `dotnet build`/`dotnet test` verdes e evidência registrada antes de marcar `[x]`.

## P0 — indispensável
- [ ] Criar solução .NET 10 com `CashFlow.Api`, `CashFlow.Worker`, módulos `Transactions`, `Consolidation`, `Shared.Contracts`, testes.
- [x] Dockerfiles e Docker Compose: API, Worker, RabbitMQ management, PostgreSQL 17 Alpine; healthchecks e volumes. *(Evidência: `docker compose up --build -d` com todos os serviços saudáveis — ver [testes](software/03-testing.md#evidência).)*
- [ ] Configurar schemas, roles com privilégios mínimos, DbContexts e migrations independentes. O runtime de cada módulo usa role restrita; migrations podem usar credenciais administrativas somente no bootstrap.
- [ ] `POST /transactions` aceita idempotency key e cria crédito/débito em transação com Outbox.
- [ ] `GET /transactions?date=...` lista lançamentos.
- [ ] Publisher de Outbox envia `TransactionCreated.v1` ao RabbitMQ com publisher confirms; falha não perde registro persistido.
- [ ] Worker consome evento, deduplica via Inbox e aplica incremento atômico no saldo do dia; ACK apenas após commit.
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
