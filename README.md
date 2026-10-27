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
| Observabilidade (P1) | `/health/live` e `/health/ready`; logs JSON com `CorrelationId` da API ao Worker; métricas por módulo | `HostTests`, `MetricsTests`, `dotnet-counters` no Compose |
| Contratos (P1) | [OpenAPI](docs/api/openapi.json) gerado pela API e [exemplo do evento](docs/api/events/transactions.transaction-created.v1.json), guardados por testes de divergência | `OpenApiContractTests`, `TransactionCreatedContractTests` |
| Isolamento entre módulos | DbContext, schema, migrations e role de banco por módulo; testes de arquitetura | `ModuleTests`, `DatabaseRoleTests` (`42501` na escrita e leitura cruzada) |
| 50 RPS com ≤ 5% de perda | Executado: perda **0,00%** com o Worker ligado e parado | [Carga](#carga-de-50-rps-no-consolidado) |

Placar: `dotnet build` 0 erros/0 warnings; `dotnet test CashFlow.slnx` **141/141** (15 arquitetura, 36 Transactions, 23 Consolidation, 67 integração com PostgreSQL e RabbitMQ reais). Detalhe de cada execução em [docs/software/03-testing.md](docs/software/03-testing.md#evidência).

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
Do clone ao primeiro saldo consolidado, em WSL/Linux/macOS (bash) ou no Windows (PowerShell). A primeira subida baixa as imagens e compila; depois disso a stack sobe em menos de um minuto (47 s num clone novo, medido em 2026-10-26).

### 1. Pré-requisitos
| | WSL2, Linux ou macOS (bash) | Windows (PowerShell) |
|---|---|---|
| Git | `git --version` | Git for Windows |
| Docker com Compose v2 | Docker Engine no WSL/Linux, ou Docker Desktop — `docker compose version` | Docker Desktop com backend WSL2 — `docker compose version` |
| .NET SDK 10 (só para build e testes fora do container) | `dotnet --version` | `dotnet --version` |
| `curl` e `jq` (só para os scripts de falha e escala) | `sudo apt install -y curl jq` | rode os scripts dentro do WSL ou no Git Bash |

O k6 não precisa ser instalado: o teste de carga roda pela imagem `grafana/k6`.

### 2. Clonar
```bash
git clone https://github.com/fabiofeti/desafio-keyrus.git
cd desafio-keyrus
```
O mesmo comando vale no PowerShell. O [.gitattributes](.gitattributes) mantém scripts e Dockerfiles em `LF` também no Windows; sem ele, o Git for Windows os converte para `CRLF` e o PostgreSQL termina com `exited (127)` ao rodar o script de roles (reproduzido e corrigido em 2026-10-26).

### 3. Criar o `.env` com senhas próprias
O `.env` é ignorado pelo Git; o [.env.example](.env.example) lista as variáveis. Troque todas as senhas `change-me`:

```bash
# bash
cp .env.example .env
for v in POSTGRES_PASSWORD TRANSACTIONS_APP_PASSWORD CONSOLIDATION_APP_PASSWORD RABBITMQ_PASSWORD; do
  sed -i "s/^$v=.*/$v=$(openssl rand -hex 16)/" .env
done
```

```powershell
# PowerShell (grava em LF e sem BOM, como o Compose espera)
Copy-Item .env.example .env
$envFile = Join-Path (Get-Location) '.env'
$content = Get-Content $envFile
foreach ($v in 'POSTGRES_PASSWORD','TRANSACTIONS_APP_PASSWORD','CONSOLIDATION_APP_PASSWORD','RABBITMQ_PASSWORD') {
  $content = $content -replace "^$v=.*", "$v=$([guid]::NewGuid().ToString('N'))"
}
[IO.File]::WriteAllText($envFile, ($content -join "`n") + "`n")
```

### 4. Conferir as portas
| Serviço | Porta padrão no host | Variável no `.env` para trocar |
|---|---|---|
| API — `/health/live`, `/health/ready` (`/health` = liveness), OpenAPI em `/openapi/v1.json` | 8080 | `API_HOST_PORT` |
| RabbitMQ management (usuário e senha do `.env`) | 15672 | `RABBITMQ_MANAGEMENT_HOST_PORT` |
| RabbitMQ AMQP | 5672 | `RABBITMQ_HOST_PORT` |
| PostgreSQL, banco `cashflow` | 5432 | `POSTGRES_HOST_PORT` |

Se alguma já estiver em uso, descomente a variável no `.env` com outra porta e use essa porta nos comandos seguintes.

```bash
ss -ltn | grep -E ':(5432|5672|15672|8080)\b'          # bash: nada listado = livres
```
```powershell
Get-NetTCPConnection -State Listen -LocalPort 5432,5672,15672,8080 -ErrorAction SilentlyContinue   # vazio = livres
```

### 5. Subir
```bash
docker compose up --build -d --wait
docker compose ps
```
Igual no PowerShell. `--wait` volta quando tudo está saudável. Estado esperado: `migrator` **Exited (0)** (aplicou as migrations e terminou), `api` **healthy**, `postgres` e `rabbitmq` **healthy**, `worker` **running**.

O `migrator` usa a credencial administrativa só para aplicar as migrations; `api` e `worker` usam as roles restritas `transactions_app` e `consolidation_app`, criadas por [deploy/postgres/init/01-roles.sh](deploy/postgres/init/01-roles.sh).

### 6. Verificar
```bash
curl -i http://localhost:8080/health/ready                # 200 Healthy
```
```powershell
Invoke-WebRequest http://localhost:8080/health/ready -UseBasicParsing | Select-Object StatusCode, Content
```
No Windows PowerShell 5.1, `curl` é apelido do `Invoke-WebRequest`; para usar os exemplos de `curl` como estão, chame `curl.exe`.

### 7. Parar e limpar
```bash
docker compose stop        # para, mantém containers e dados
docker compose down        # remove containers e rede, mantém os volumes (dados)
docker compose down -v     # remove também os volumes: apaga o banco e as filas
```

### Problemas comuns
| Sintoma | Causa e solução |
|---|---|
| `postgres` `exited (127)` logo após `01-roles.sh` | Script com `CRLF` (clone anterior ao `.gitattributes`). Clone de novo, ou, num clone sem alterações locais: `git pull`, depois `git rm --cached -r -q .` e `git reset --hard` (apaga alterações não commitadas). `git checkout -- .` **não** resolve; e rode `docker compose down -v` antes de subir, porque o banco criado sem as roles fica no volume |
| `required variable ... is missing a value: set in .env` | Falta o `.env` (passo 3) |
| `port is already allocated` | Porta ocupada por outra aplicação; troque no `.env` (passo 4) |
| `curl` do PowerShell pede parâmetros ou responde diferente | Use `curl.exe` ou `Invoke-RestMethod` |
| Worker registra `PRECONDITION_FAILED` ao declarar a fila | Volume antigo do RabbitMQ, de antes da DLQ; ver [Operação da fila](#operação-da-fila) |
| Testes de integração falham ao subir containers | O Docker precisa estar acessível de onde o `dotnet test` roda (Docker Desktop no Windows, ou rode dentro do WSL) |

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

No PowerShell, os mesmos chamados:

```powershell
$api = 'http://localhost:8080'
$body = @{ type = 'Credit'; amount = 150.00; occurredAt = '2026-10-30T10:00:00-03:00'; description = 'Venda balcão' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$api/transactions" -ContentType 'application/json' -Headers @{ 'Idempotency-Key' = 'venda-0001' } -Body $body
Invoke-RestMethod "$api/transactions?date=2026-10-30&page=1&pageSize=50"
Invoke-RestMethod "$api/daily-balances/2026-10-30"
```
Num erro (400/409), o `Invoke-RestMethod` lança exceção; o corpo em *problem details* está em `$_.ErrorDetails.Message`.

Toda resposta traz `X-Correlation-Id` (o do cliente, se bem formado, ou um gerado). Ele segue no evento (`correlationId` e propriedade AMQP) e aparece como campo `CorrelationId` em cada linha de log da API e do Worker — ver [Observabilidade](#observabilidade).

### Observabilidade
- **Probes:** `/health/live` só confirma que o processo responde; `/health/ready` abre uma conexão por módulo com a própria role e devolve `503` se o PostgreSQL de um deles não responder. O RabbitMQ não entra: a API não depende dele.
- **Logs:** no Compose, uma linha JSON por evento, com escopos. Para seguir um request da API até a consolidação:

```bash
curl -X POST http://localhost:8080/transactions -H 'Content-Type: application/json' \
  -H 'X-Correlation-Id: demo-123' -H 'Idempotency-Key: demo-123' \
  -d '{"type":"Credit","amount":10.00,"occurredAt":"2026-11-02T10:00:00-03:00"}'
docker compose logs api worker --no-log-prefix | grep demo-123 | jq -c '{Category, Message, scope: .Scopes[-1]}'
# API:    "Executing command CreateTransactionCommand"            scope {"CorrelationId":"demo-123"}
# Worker: "Executing command ApplyTransactionToDailyBalanceCommand" scope {"CorrelationId":"demo-123","MessageId":"…","MessageType":"transactions.transaction-created.v1"}
```

- **Métricas:** um `Meter` por módulo (`CashFlow.Transactions`, `CashFlow.Consolidation`, `CashFlow.Messaging`): lançamentos criados e replays, Outbox publicada/falhas/circuito aberto, eventos aplicados/duplicados, **atraso da consolidação** (histograma) e mensagens na DLQ. Sem exportador no MVP; leia com `dotnet-counters` dentro do container (imagem Alpine, binário musl):

```bash
curl -sL -o dotnet-counters https://aka.ms/dotnet-counters/linux-musl-x64 && chmod +x dotnet-counters
docker compose cp dotnet-counters worker:/tmp/dotnet-counters
docker compose exec worker /tmp/dotnet-counters monitor -p 1 --counters CashFlow.Transactions,CashFlow.Consolidation,CashFlow.Messaging
```

Observado (2026-10-26, 20 POSTs + 1 replay em 40 s): API `created` 15 `Credit` + 5 `Debit`, `replayed` 1; Worker `outbox.published` 20, `events_applied` 20, atraso p50 1,03 s e p95 1,14 s (o intervalo de polling da Outbox é 1 s). Instrumentos e escopo em [02-quality-attributes](docs/software/02-quality-attributes.md#observabilidade-implementado).

### Build e testes
```bash
dotnet build CashFlow.slnx
dotnet test CashFlow.slnx                                      # tudo; integração sobe PostgreSQL e RabbitMQ em containers
dotnet test CashFlow.slnx --filter "Category!=Integration"     # só arquitetura e unitários
```
Os mesmos comandos funcionam no PowerShell. Os testes de integração sobem PostgreSQL e RabbitMQ próprios (Testcontainers), independentes da stack do Compose.

### Teste de falha (Worker parado)
Os scripts de `tests/chaos/` são bash: no Windows, rode-os dentro do WSL ou no Git Bash (precisam de `curl` e `jq`).

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
No PowerShell (sem o `-u`, que é do Linux):
```powershell
docker run --rm -i --network cashflow_default -e BASE_URL=http://api:8080 -e DATE=2026-10-29 -v "${PWD}/tests/load:/scripts" grafana/k6 run /scripts/daily-balances.js
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
- **Sem autenticação nem rate limiting:** todos os endpoints são públicos por decisão do MVP; JWT/OAuth2 é evolução, e o rate limiting está adiado de propósito até haver métrica de consumo ([plano por IP, por usuário do Entra ID e com semáforo](docs/software/04-rate-limiting-and-concurrency.md)).
- **Mensagem envenenada** segura a fila por ~40 s de backoff antes de ir à DLQ; replay da DLQ é manual.
- **Carga medida localmente**, numa só máquina e sobre uma única data; não substitui um teste em ambiente de produção.
- **Métricas sem exportador:** lidas com `dotnet-counters`; Prometheus/OTLP, dashboards e alertas são evolução. Profundidade da fila e idade da Outbox não são métricas (management UI e SQL).
- Itens P2 (HA, autenticação, replay automatizado da DLQ, dashboards, custos) estão só documentados, com proposta por item e o procedimento para extrair um módulo para microsserviço, em [04-evolution-and-module-extraction](docs/solucoes/04-evolution-and-module-extraction.md). A escala horizontal do Worker foi medida: 3 réplicas, soma exata, nenhum evento publicado ou aplicado duas vezes ([scale-workers.sh](tests/chaos/scale-workers.sh)).

## Documentação
- [Plano de implementação e estado atual](docs/IMPLEMENTATION-PLAN.md)
- [Soluções: negócio e requisitos](docs/solucoes/01-business-and-requirements.md)
- [Soluções: arquitetura e trade-offs](docs/solucoes/02-target-architecture.md)
- [Soluções: riscos, custos e evolução](docs/solucoes/03-operations-and-roadmap.md)
- [Soluções: P2 e extração de módulo para microsserviço](docs/solucoes/04-evolution-and-module-extraction.md)
- [Software: design, dados e contratos](docs/software/01-design-and-contracts.md)
- [Software: resiliência, segurança e NFRs](docs/software/02-quality-attributes.md)
- [Software: testes, critérios de aceite e evidências](docs/software/03-testing.md)
- [Software: rate limiting e semáforo (proposta adiada)](docs/software/04-rate-limiting-and-concurrency.md)
- [Contratos: OpenAPI](docs/api/openapi.json) e [evento TransactionCreated.v1](docs/api/events/transactions.transaction-created.v1.json)
- [C4 e sequências em Mermaid](docs/diagrams/architecture.md)
- [ADR: arquitetura e isolamento](docs/adr/ADR-001.md)

## Escopo e transparência
O que está marcado como entregue foi executado, e cada número citado saiu de uma execução registrada em [docs/software/03-testing.md](docs/software/03-testing.md#evidência). O que não foi executado está dito como tal acima. Não há sistema legado no enunciado; nenhuma arquitetura de transição foi inventada. Credenciais ficam no `.env` local, fora do Git.
