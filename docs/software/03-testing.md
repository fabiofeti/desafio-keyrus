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
| Compose sobe | `docker compose up --build -d` | migrator `Exited (0)`, api healthy, worker/postgres/rabbitmq up; `/health` e `/openapi/v1.json` 200 (2026-10-26); repetido com os dois módulos: migrator `Exited (0)` executando `MigrateAsync` de cada módulo (ainda sem migrations), api healthy, worker estável | Portas do host configuráveis no `.env` |
| Integração | Pendente (publicação da Outbox, Inbox, saldo) | Não executado | — |
| Queda Worker | Pendente | Não executado | — |
| Queda broker | Pendente | Não executado | — |
| Carga 50 RPS | Pendente | Não executado | — |

Não marcar sucesso sem execução.
