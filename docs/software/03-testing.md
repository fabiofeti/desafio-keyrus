# Software — estratégia de testes e aceite

## Testes unitários
- Débito/crédito e valor positivo; saldo correto e precisão decimal.
- Data de negócio; entradas inválidas.
- Idempotency key repetida e payload divergente.

## Testes de integração
- EF Core + PostgreSQL real em container.
- POST grava lançamento e Outbox atomicamente.
- Publisher entrega evento e marca Outbox apenas após confirmação.
- Consumer duplica evento sem duplicar saldo (Inbox).
- Role Transactions não pode INSERT/UPDATE em `consolidation`; role Consolidation não pode gravar em `transactions`.

## Resiliência / caos local
1. Subir Compose e registrar baseline.
2. Parar Worker; executar POST de lançamentos e confirmar sucesso e Outbox pendente/fila acumulada.
3. Reiniciar Worker; aguardar convergência e conferir soma exata.
4. Interromper RabbitMQ, continuar POST, restaurar broker e verificar publicação posterior.
5. Redeliver mesmo eventId e confirmar saldo inalterado.

## Carga: 50 RPS
Testar `GET /daily-balances/{date}` por janela representativa (por exemplo 2 minutos), com gerador k6 ou equivalente. Registrar ambiente, script, total esperado/enviado, respostas 2xx, 4xx, 5xx, timeouts, RPS efetivo, p95/p99 e duração.

**Critério proposto de perda HTTP:** `(requisições sem resposta válida / requisições enviadas) * 100 <= 5%`; separar erros funcionais 4xx de falhas técnicas e definir a interpretação antes de executar. O enunciado não fixa percentil de latência; usar metas internas identificadas como propostas.

## Evidência
Preencher após rodar:
| Cenário | Comando/script | Resultado | Evidência |
|---|---|---|---|
| Arquitetura (camadas, registro de módulos, hosts) | `dotnet test tests/CashFlow.ArchitectureTests` | 15/15 passaram (2026-10-26), com os dois módulos registrados | Inclui: API sem dependência do broker; Worker sem mapear endpoint; Common sem referência a módulo |
| Isolamento por role (sem módulos ainda) | `dotnet test tests/CashFlow.IntegrationTests` — `DatabaseRoleTests` | 10/10 passaram (2026-10-26) | Com o script real de init: cada role lê/grava o próprio schema; `42501` ao ler/gravar o outro, ao `DELETE` e ao `CREATE TABLE` |
| Publisher com confirms (base) | `RabbitMqPublisherTests` | 2/2 passaram (2026-10-26) | Mensagem persistente com id/tipo/correlation; publish sem fila ligada lança `PublishException` |
| Módulos Transactions e Consolidation registrados | `dotnet test tests/CashFlow.Modules.{Transactions,Consolidation}.UnitTests` e `ModuleDatabaseTests` (integração) | 2/2 + 2/2 unitários e 4/4 integração passaram (2026-10-26) | Sem a connection string do próprio módulo o registro falha (não cai numa string compartilhada); DbContext/UoW são a mesma instância por escopo; a API conecta como `transactions_app` e `consolidation_app`, cada uma no seu contexto; histórico de migrations em `{schema}.__ef_migrations_history` |
| Fronteira entre módulos detecta violação | Referência temporária de `Consolidation.Application` a `Transactions.Domain`, depois revertida | `Module_Should_NotReferenceAnotherModulesInternals` falhou com "reaches into another module: CashFlow.Modules.Transactions.Domain"; 15/15 após reverter (2026-10-26) | Prova de que a regra não é vazia com dois módulos |
| Lançamentos: domínio, validator, handler | `dotnet test tests/CashFlow.Modules.Transactions.UnitTests` | 34/34 passaram (2026-10-26) | Invariantes do agregado; uma regra por teste no validator; handler sobre Sqlite afirma lançamento **e** Outbox, replay, conflito 409 e nada gravado quando o agregado recusa; fingerprint normaliza valor e instante; data de negócio no fuso de São Paulo |
| Lançamentos por HTTP | `TransactionsTests` (integração, PostgreSQL real) | 14/14 passaram (2026-10-26) | 201 + listagem na data de negócio; uma linha na Outbox, não publicada; replay com `Idempotency-Replayed`; 8 POSTs simultâneos com a mesma chave criam 1 lançamento e 1 mensagem; 409; 400 para chave ausente, payload inválido, JSON inválido e query inválida; paginação ordenada |
| Smoke no Compose | `curl` em `http://localhost:18080` | 201, replay 201 com `Idempotency-Replayed: true`, 409, `GET` 200, data inválida 400; `transactions.outbox_messages` com 1 linha não publicada (2026-10-26) | Migration `Add_Transactions_And_Outbox` aplicada pelo migrator; `has-pending-model-changes`: nenhuma |
| Consolidação: comando e evento | `dotnet test tests/CashFlow.Modules.Consolidation.UnitTests` | 16/16 passaram (2026-10-26) | Crédito/débito vão para a coluna certa; duplicado devolve sucesso (ACK); falha do comando vira `IntegrationEventHandlingException` (NACK); validator recusa evento malformado |
| Outbox → RabbitMQ → Inbox | `ConsolidationFlowTests` (integração, PostgreSQL e RabbitMQ reais, Worker real) | 5/5 passaram, duas execuções seguidas (2026-10-26) | 5 POSTs concorrentes chegam ao saldo com a soma exata, 5 linhas na Inbox, 5 publicadas; o mesmo evento publicado 2× no broker soma 1×; 30 eventos + 10 entregas concorrentes do mesmo evento somam exato e geram 30 linhas na Inbox; broker inalcançável: `attempt_count` 1, `published_on_utc` nulo, `last_error` gravado; broker de volta: publicada, `attempt_count` 2; handler que falha deixa a mensagem na fila com `Redelivered` e nada aplicado |
| Isolamento por role nas tabelas reais | `DatabaseRoleTests` | 5/5 novos passaram (2026-10-26) | `consolidation_app` não lê `transactions.transactions`/`outbox_messages` nem altera a Outbox; `transactions_app` não lê `consolidation.daily_balances`/`inbox_messages` (`42501`) |
| Fluxo completo no Compose | 4 `curl` POST em 2026-10-27 + `psql` | `daily_balances` 2026-10-27: credits 345.10, debits 125.10, saldo 220.00 (esperado: 300+45,10 e 120,35+4,75); 5/5 mensagens publicadas com `attempt_count` 1; 5 linhas na Inbox; fila com 0 prontas e 0 sem ACK (2026-10-26) | O lançamento deixado pendente na etapa anterior foi publicado e consolidado assim que o Worker subiu |
| Compose sobe | `docker compose up --build -d` | migrator `Exited (0)`, api healthy, worker/postgres/rabbitmq up; `/health` e `/openapi/v1.json` 200 (2026-10-26); repetido com os dois módulos: migrator `Exited (0)` executando `MigrateAsync` de cada módulo (ainda sem migrations), api healthy, worker estável | Portas do host configuráveis no `.env` |
| Queda Worker | Pendente | Não executado | — |
| Queda broker | Pendente | Não executado | — |
| Carga 50 RPS | Pendente | Não executado | — |

Não marcar sucesso sem execução.
