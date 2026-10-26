# Keyrus Cash Flow — Desafio de Arquitetura

Prova técnica única com duas perspectivas: [Arquiteto de Soluções](docs/solucoes/01-business-and-requirements.md) e [Arquiteto de Software](docs/software/01-design-and-contracts.md).

## Problema
Registrar lançamentos financeiros (créditos/débitos) e consultar o saldo diário consolidado. A indisponibilidade do processamento de consolidação não pode interromper o registro. O enunciado fixa pico de **50 requisições/segundo** na consulta do consolidado com **no máximo 5% de perda** — aqui comprovado por teste executado, com os números abaixo.

## O que foi entregue
| Requisito | Situação | Evidência |
|---|---|---|
| Registrar crédito/débito com idempotência | `POST /transactions` com `Idempotency-Key` (replay, 409, corrida resolvida pelo índice único) | `TransactionsTests`, handler e validator |
| Listar lançamentos do dia | `GET /transactions?date=` paginado | `TransactionsTests` |
| Saldo diário consolidado | `GET /daily-balances/{date}`, consistência eventual explícita | `DailyBalancesTests`, smoke no Compose |
| Registro não depende da consolidação | API não fala com o broker; lançamento + Outbox numa transação local | Testes de falha com o Worker e com o RabbitMQ parados; teste de atomicidade |
| Sem perda nem duplicação na consolidação | Outbox com publisher confirms → RabbitMQ (quorum + DLQ) → Inbox com incremento atômico | `ConsolidationFlowTests`, redelivery no Compose |
| Isolamento entre módulos | DbContext, schema, migrations e role de banco por módulo; testes de arquitetura | `ModuleTests`, `DatabaseRoleTests` (`42501` na escrita e leitura cruzada) |
| 50 RPS com ≤ 5% de perda | Executado: perda **0,00%** com o Worker ligado e parado | [Carga](#carga-de-50-rps-no-consolidado) |

Placar: `dotnet build` 0 erros/0 warnings; `dotnet test CashFlow.slnx` **126/126** (15 arquitetura, 34 Transactions, 19 Consolidation, 58 integração com PostgreSQL e RabbitMQ reais). Detalhe de cada execução em [docs/software/03-testing.md](docs/software/03-testing.md#evidência).

## Arquitetura
Monólito modular .NET 10 com dois processos: **API** (Minimal APIs) e **Worker** (publisher da Outbox e consumidor RabbitMQ), que hospedam os mesmos módulos sem duplicar código. Dois bounded contexts, **Transactions** e **Consolidation**, no mesmo PostgreSQL 17, cada um com schema, `DbContext`, migrations e role próprios. Um módulo nunca lê nem grava tabelas do outro: Consolidation conhece apenas o contrato versionado `TransactionCreated.v1`.

```text
POST /transactions ─► API ─► transactions.transactions + transactions.outbox_messages   (uma transação)
                                         │
                       Worker: OutboxProcessor ─► RabbitMQ cashflow.transactions (confirm)
                                                        │
                       Worker: consumer ◄─ consolidation.transaction-created.v1 (quorum, DLQ)
                                         │
                consolidation.inbox_messages + consolidation.daily_balances   (uma instrução SQL)
GET /daily-balances/{date} ─► API ─► consolidation.daily_balances
```

Diagramas C4 e sequências em [docs/diagrams/architecture.md](docs/diagrams/architecture.md); decisões em [ADR-001](docs/adr/ADR-001.md).

```text
src/Common/          Common.Domain | Application | Infrastructure | Presentation
src/Modules/{Transactions,Consolidation}/   Domain, Application, Infrastructure, Presentation, IntegrationEvents
src/Api, src/Worker  hosts (CashFlow.Hosting/Modules.cs lista os módulos)
tests/               ArchitectureTests, Modules.*.UnitTests, IntegrationTests, chaos/, load/
deploy/postgres/init/01-roles.sh   schemas e roles com privilégios mínimos
```

## Execução local
Pré-requisitos: Docker (com Compose) e, para build/testes fora do container, .NET SDK 10.

```bash
cp .env.example .env        # troque as senhas; o .env é ignorado pelo Git
docker compose up --build -d
docker compose ps           # migrator: Exited (0); api: healthy; worker, postgres, rabbitmq: up
```

| Serviço | URL / porta padrão no host | Variável no `.env` para trocar |
|---|---|---|
| API | http://localhost:8080 — `/health`, OpenAPI em `/openapi/v1.json` | `API_HOST_PORT` |
| RabbitMQ management | http://localhost:15672 (usuário/senha do `.env`) | `RABBITMQ_MANAGEMENT_HOST_PORT` |
| RabbitMQ AMQP | 5672 | `RABBITMQ_HOST_PORT` |
| PostgreSQL | 5432, banco `cashflow` | `POSTGRES_HOST_PORT` |

O serviço `migrator` aplica as migrations com a credencial administrativa e termina; `api` e `worker` usam as roles restritas `transactions_app` e `consolidation_app`, criadas por [deploy/postgres/init/01-roles.sh](deploy/postgres/init/01-roles.sh). Se uma porta já estiver ocupada por outra stack, defina a variável correspondente no `.env` e use a nova porta nos comandos.

### Usando a API
Saídas abaixo são de uma execução real (2026-10-26), com ids e horários daquela execução.

```bash
# Registrar um crédito (a Idempotency-Key é obrigatória: [A-Za-z0-9_-]{1,100})
curl -i -X POST http://localhost:8080/transactions \
  -H 'Content-Type: application/json' -H 'Idempotency-Key: venda-0001' \
  -d '{"type":"Credit","amount":150.00,"occurredAt":"2026-10-30T10:00:00-03:00","description":"Venda balcão"}'
# HTTP/1.1 201 Created
# Location: /transactions/01a17a9a-8943-79da-bc2f-bc477ad739e9
# {"id":"01a17a9a-…","type":"Credit","amount":150.00,"occurredAt":"2026-10-30T13:00:00+00:00","businessDate":"2026-10-30","description":"Venda balcão"}
```

- Repetir o mesmo comando devolve o mesmo corpo, `201` e `Idempotency-Replayed: true`.
- Mesma chave com outro payload: `409`, `"code":"Transactions.IdempotencyKeyReused"`.
- Sem chave, valor ≤ 0 ou com mais de 2 casas, `type` diferente de `Credit`/`Debit`, JSON inválido: `400` em *problem details* com `code` e `errors`.
- `businessDate` é o dia no fuso `America/Sao_Paulo` (configurável em `BusinessCalendar:TimeZone`).

```bash
# Um débito no mesmo dia
curl -X POST http://localhost:8080/transactions \
  -H 'Content-Type: application/json' -H 'Idempotency-Key: fornecedor-0001' \
  -d '{"type":"Debit","amount":45.90,"occurredAt":"2026-10-30T14:30:00-03:00","description":"Fornecedor"}'

# Lançamentos do dia (page ≥ 1, pageSize 1..200, padrão 50; ordenado por occurredAt, id)
curl 'http://localhost:8080/transactions?date=2026-10-30&page=1&pageSize=50'
# {"items":[{…"Credit"…150.00…},{…"Debit"…45.90…}],"page":1,"pageSize":50,"totalCount":2}

# Saldo consolidado do dia (atualizado pelo Worker; nos testes locais, em 1 a 2 s)
curl http://localhost:8080/daily-balances/2026-10-30
# {"date":"2026-10-30","credits":150.00,"debits":45.90,"balance":104.10,"lastUpdatedAt":"2026-10-26T23:45:11.935758Z","consistency":"eventual"}

# Dia sem lançamento consolidado: 200 com zeros, não 404
curl http://localhost:8080/daily-balances/2030-01-01
# {"date":"2030-01-01","credits":0,"debits":0,"balance":0,"lastUpdatedAt":null,"consistency":"eventual"}
```

Toda resposta traz `X-Correlation-Id` (o do cliente, se bem formado, ou um gerado). Ele segue no evento (`correlationId` e propriedade AMQP) e é restaurado no processamento do consumidor; ainda não entra nos logs como campo estruturado (item P1).

### Build e testes
```bash
dotnet build CashFlow.slnx
dotnet test CashFlow.slnx                                      # tudo; integração sobe PostgreSQL e RabbitMQ em containers
dotnet test CashFlow.slnx --filter "Category!=Integration"     # só arquitetura e unitários
```

### Teste de falha (Worker parado)
Com a stack no ar, [tests/chaos/worker-down.sh](tests/chaos/worker-down.sh) para o Worker, envia 100 lançamentos, religa o Worker, confere a soma exata no `GET /daily-balances/{date}`, Outbox vazia e uma linha na Inbox por evento, e republica um evento já aplicado para provar que a redelivery não muda o saldo. Sai com código ≠ 0 na primeira expectativa quebrada.

```bash
BASE_URL=http://localhost:8080 tests/chaos/worker-down.sh    # opcionais: DATE, N, TIMEOUT, MGMT_URL
```

Resultado observado (2026-10-26, duas execuções): 100/100 POST `201` com o Worker parado (~1,7 s), 100 linhas pendentes na Outbox, saldo inalterado; convergência em ~2 s após religar, soma exata, Outbox 0 pendente, Inbox = eventos; redelivery ignorada (log do Worker: `already applied`), DLQ 0 → 0. Log em [tests/chaos/results/worker-down.log](tests/chaos/results/worker-down.log).

### Teste de falha (RabbitMQ parado)
[tests/chaos/broker-down.sh](tests/chaos/broker-down.sh) para o RabbitMQ, envia 100 lançamentos, mantém o broker fora por mais `OUTAGE` segundos (padrão 30), religa e confere: POSTs `201`, Outbox retendo os eventos, Worker vivo com o circuit breaker aberto, reconexão do publisher e do consumidor, soma exata, Outbox vazia, Inbox = eventos, fila drenada e DLQ inalterada.

```bash
BASE_URL=http://localhost:8080 tests/chaos/broker-down.sh    # opcionais: DATE, N, OUTAGE, TIMEOUT, MGMT_URL
```

Resultado observado (2026-10-26, duas execuções): 100/100 POST `201` com o broker parado (~2 s no total); 100 pendentes na Outbox; o Polly esgotou o timeout de 5 s, registrou a tentativa e abriu o breaker (`Outbox publishing paused: the broker circuit is open`); convergência 11–15 s depois de religar o RabbitMQ (backoff de reconexão + breaker), soma exata, DLQ 0 → 0. Log em [tests/chaos/results/broker-down.log](tests/chaos/results/broker-down.log).

### Carga de 50 RPS no consolidado
[tests/load/daily-balances.js](tests/load/daily-balances.js) (k6, `constant-arrival-rate` 50/s por 2 min), executado pela imagem do k6 na rede do Compose. O critério foi fixado no script antes da execução: resposta válida = `200` com o JSON do saldo da data em até 2 s; 5xx, timeout, erro de conexão e iteração descartada contam como perda; limite 5%.

```bash
docker run --rm -i --network cashflow_default -u "$(id -u)" \
  -e BASE_URL=http://api:8080 -e DATE=2026-10-29 -v "$PWD/tests/load:/scripts" \
  grafana/k6 run --summary-export=/scripts/results/worker-up.json /scripts/daily-balances.js
```

| Cenário (2026-10-26) | Enviadas | Válidas | Perda | RPS efetivo | p95 | p99 |
|---|---|---|---|---|---|---|
| Worker ligado | 6001 | 6001 | **0,00%** | 50,09 | 1,99 ms | 2,54 ms |
| Worker parado | 6000 | 6000 | **0,00%** | 50,08 | 1,87 ms | 2,46 ms |

Ambiente: AMD Ryzen 7 5700X, 16 GB, WSL2, Docker 29.2, k6 v2.3.0. Gerador e sistema na mesma máquina, uma data só e sem escritas concorrentes: os números valem para este ambiente local, não são projeção de produção ([critério, ambiente e limites](docs/software/03-testing.md#carga-50-rps); logs em `tests/load/results/`).

### Operação da fila
A consolidação consome a fila *quorum* `consolidation.transaction-created.v1`. Uma mensagem que falha `RabbitMq__DeliveryLimit` vezes (padrão 5, com backoff de 1, 2, 5 e 10 s) vai para `consolidation.transaction-created.v1.dlq`; corpo ilegível vai direto. O reprocessamento da DLQ é manual, pela UI de management. Num volume do RabbitMQ criado antes da DLQ, o Worker registra `PRECONDITION_FAILED` ao declarar a fila; com a fila antiga vazia, recrie-a uma vez:

```bash
docker compose exec rabbitmq rabbitmqctl delete_queue --if-empty consolidation.transaction-created.v1
```

## Limitações conhecidas
- **PostgreSQL compartilhado:** os módulos são isolados por schema e role, mas estão no mesmo servidor; uma queda do banco derruba registro e consulta (limitação aceita no [ADR-001](docs/adr/ADR-001.md)).
- **Sem autenticação:** todos os endpoints são públicos por decisão do MVP; JWT/OAuth2 é evolução.
- **Mensagem envenenada** segura a fila por ~40 s de backoff antes de ir à DLQ; replay da DLQ é manual.
- **Carga medida localmente**, numa só máquina e sobre uma única data; não substitui um teste em ambiente de produção.
- Itens P1 restantes (health/readiness separado, logs estruturados com correlation ID, métricas, contratos documentados a partir do código) e P2 (escala horizontal, HA, dashboards, custos) estão no [plano](docs/IMPLEMENTATION-PLAN.md) e não foram implementados.

## Documentação
- [Plano de implementação e estado atual](docs/IMPLEMENTATION-PLAN.md)
- [Soluções: negócio e requisitos](docs/solucoes/01-business-and-requirements.md)
- [Soluções: arquitetura e trade-offs](docs/solucoes/02-target-architecture.md)
- [Soluções: riscos, custos e evolução](docs/solucoes/03-operations-and-roadmap.md)
- [Software: design, dados e contratos](docs/software/01-design-and-contracts.md)
- [Software: resiliência, segurança e NFRs](docs/software/02-quality-attributes.md)
- [Software: testes, critérios de aceite e evidências](docs/software/03-testing.md)
- [C4 e sequências em Mermaid](docs/diagrams/architecture.md)
- [ADR: arquitetura e isolamento](docs/adr/ADR-001.md)

## Escopo e transparência
O que está marcado como entregue foi executado, e cada número citado saiu de uma execução registrada em [docs/software/03-testing.md](docs/software/03-testing.md#evidência). O que não foi executado está dito como tal acima. Não há sistema legado no enunciado; nenhuma arquitetura de transição foi inventada. Credenciais ficam no `.env` local, fora do Git.
