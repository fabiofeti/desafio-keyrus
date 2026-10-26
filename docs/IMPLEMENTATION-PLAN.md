# Plano de execução para Claude Code

## P0 — indispensável
- [ ] Criar solução .NET 10 com `CashFlow.Api`, `CashFlow.Worker`, módulos `Transactions`, `Consolidation`, `Shared.Contracts`, testes.
- [ ] Dockerfiles e Docker Compose: API, Worker, RabbitMQ management, PostgreSQL 17 Alpine; healthchecks e volumes.
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
