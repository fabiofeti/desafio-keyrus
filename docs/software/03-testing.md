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
| Arquitetura (camadas, registro de módulos, hosts) | `dotnet test tests/CashFlow.ArchitectureTests` | 15/15 passaram (2026-10-26) | Inclui: API sem dependência do broker; Worker sem mapear endpoint; Common sem referência a módulo |
| Isolamento por role (sem módulos ainda) | `dotnet test tests/CashFlow.IntegrationTests` — `DatabaseRoleTests` | 10/10 passaram (2026-10-26) | Com o script real de init: cada role lê/grava o próprio schema; `42501` ao ler/gravar o outro, ao `DELETE` e ao `CREATE TABLE` |
| Publisher com confirms (base) | `RabbitMqPublisherTests` | 2/2 passaram (2026-10-26) | Mensagem persistente com id/tipo/correlation; publish sem fila ligada lança `PublishException` |
| Compose sobe | `docker compose up --build -d` | migrator `Exited (0)`, api healthy, worker/postgres/rabbitmq up; `/health` e `/openapi/v1.json` 200 (2026-10-26) | Portas do host configuráveis no `.env` |
| Unitários | Pendente | Não executado | — |
| Integração | Pendente (fluxos de lançamento, Outbox, Inbox) | Não executado | — |
| Queda Worker | Pendente | Não executado | — |
| Queda broker | Pendente | Não executado | — |
| Carga 50 RPS | Pendente | Não executado | — |

Não marcar sucesso sem execução.
