# Keyrus Cash Flow — Desafio de Arquitetura

Prova técnica única com duas perspectivas: [Arquiteto de Soluções](docs/solucoes/01-business-and-requirements.md) e [Arquiteto de Software](docs/software/01-design-and-contracts.md).

## Problema
Registrar lançamentos financeiros (créditos/débitos) e consultar saldo diário consolidado. A indisponibilidade do processamento de consolidação não deve interromper o registro. O desafio estabelece pico de **50 requisições/segundo** para consolidação e **máximo de 5% de perda**; comprovar por teste, não por suposição.

## Arquitetura proposta
Monólito modular .NET 10: API e worker compartilhando os módulos de domínio, PostgreSQL com schemas `transactions` e `consolidation`, RabbitMQ e Outbox/Inbox. Consistência eventual; ausência de escrita cruzada.

## Execução local
Pré-requisitos: .NET SDK 10 e Docker.

```bash
cp .env.example .env        # troque os valores; o .env é ignorado pelo Git
docker compose up --build -d
docker compose ps           # migrator: Exited (0); api: healthy; worker, postgres, rabbitmq: up
curl -i http://localhost:8080/health
```

| Serviço | Porta padrão no host | Variável no `.env` para trocar |
|---|---|---|
| API (`/health`, OpenAPI em `/openapi/v1.json`) | 8080 | `API_HOST_PORT` |
| PostgreSQL | 5432 | `POSTGRES_HOST_PORT` |
| RabbitMQ AMQP / management | 5672 / 15672 | `RABBITMQ_HOST_PORT` / `RABBITMQ_MANAGEMENT_HOST_PORT` |

O serviço `migrator` aplica as migrations com a credencial administrativa e termina; `api` e `worker` usam as roles restritas `transactions_app` e `consolidation_app`, criadas por [deploy/postgres/init/01-roles.sh](deploy/postgres/init/01-roles.sh).

```bash
dotnet build CashFlow.slnx
dotnet test CashFlow.slnx                                      # tudo; integração precisa de Docker
dotnet test CashFlow.slnx --filter "Category!=Integration"     # só arquitetura/unitários
```

**Operação da fila.** A consolidação consome a fila *quorum* `consolidation.transaction-created.v1`; mensagens que falham mais de `RabbitMq__DeliveryLimit` vezes (padrão 5) vão para `consolidation.transaction-created.v1.dlq` (veja na UI de management, porta 15672) e o reprocessamento é manual. Num volume do RabbitMQ criado antes da DLQ, o Worker registra `PRECONDITION_FAILED` ao declarar a fila; com a fila antiga vazia, recrie-a uma vez:

```bash
docker compose exec rabbitmq rabbitmqctl delete_queue --if-empty consolidation.transaction-created.v1
```

**Teste de falha (Worker parado).** Com a stack no ar, [tests/chaos/worker-down.sh](tests/chaos/worker-down.sh) para o Worker, envia 100 lançamentos, religa o Worker, confere a soma exata no `GET /daily-balances/{date}`, Outbox vazia e uma linha na Inbox por evento, e republica um evento já aplicado para provar que a redelivery não muda o saldo. Sai com código ≠ 0 na primeira expectativa quebrada.

```bash
BASE_URL=http://localhost:8080 tests/chaos/worker-down.sh    # opcionais: DATE, N, TIMEOUT, MGMT_URL
```

Resultado observado (2026-10-26, duas execuções): 100/100 POST `201` com o Worker parado (~1,7 s), 100 linhas pendentes na Outbox, saldo inalterado; convergência em ~2 s após religar, soma exata, Outbox 0 pendente, Inbox = eventos; redelivery ignorada, DLQ 0 → 0. Log em [tests/chaos/results/worker-down.log](tests/chaos/results/worker-down.log).

**Status:** lançamentos (`POST`/`GET /transactions`), Outbox → RabbitMQ → Inbox, `GET /daily-balances/{date}` e teste de falha do Worker prontos e verificados; faltam a carga de 50 RPS e o README final (ver [plano](docs/IMPLEMENTATION-PLAN.md)).

## Documentação
- [Plano de implementação](docs/IMPLEMENTATION-PLAN.md)
- [Soluções: negócio e requisitos](docs/solucoes/01-business-and-requirements.md)
- [Soluções: arquitetura e trade-offs](docs/solucoes/02-target-architecture.md)
- [Soluções: riscos, custos e evolução](docs/solucoes/03-operations-and-roadmap.md)
- [Software: design, dados e contratos](docs/software/01-design-and-contracts.md)
- [Software: resiliência, segurança e NFRs](docs/software/02-quality-attributes.md)
- [Software: testes e critérios de aceite](docs/software/03-testing.md)
- [C4 e sequências em Mermaid](docs/diagrams/architecture.md)
- [ADR: arquitetura e isolamento](docs/adr/ADR-001.md)

## Escopo e transparência
Os documentos descrevem decisões de projeto propostas, não uma implementação já verificada. Atualizar evidências conforme o desenvolvimento. Não há migração legada real no enunciado; arquitetura de transição é opcional e não será inventada.
