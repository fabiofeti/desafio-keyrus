# Soluções — evolução (P2) e extração de um módulo para microsserviço

O P2 do [plano](../IMPLEMENTATION-PLAN.md) é só documentação: nenhum item exige código para o desafio. A única coisa executada aqui foi uma **medição** que sustenta o texto: o Worker rodando em 3 réplicas ([tests/chaos/scale-workers.sh](../../tests/chaos/scale-workers.sh), resultado abaixo).

## Escalar não é o mesmo que extrair
A primeira resposta para "esse módulo precisa de mais capacidade" é **mais réplicas do mesmo deployable**, não um microsserviço novo. O código já foi feito para isso:

- **Worker:** o publisher trava as linhas da Outbox com `FOR UPDATE SKIP LOCKED` ([OutboxProcessor](../../src/Modules/Transactions/CashFlow.Modules.Transactions.Infrastructure/Outbox/OutboxProcessor.cs)), a fila quorum aceita consumidores concorrentes e a Inbox aplica cada evento numa instrução atômica ([DailyBalanceProjection](../../src/Modules/Consolidation/CashFlow.Modules.Consolidation.Infrastructure/DailyBalances/DailyBalanceProjection.cs)). A soma do saldo é comutativa, então a ordem entre réplicas não importa.
- **API:** sem estado em memória; a idempotência é o índice único de `idempotency_key` no banco, válida entre réplicas. Precisa de um balanceador na frente (no Compose a porta do host é fixa, por isso não foi medido com réplicas).

**Medido (2026-10-26, duas execuções):** `docker compose up --scale worker=3`, 300 POSTs com 20 em paralelo, todos `201` em ~1,2 s; saldo convergiu 1 s depois do último POST para a soma exata; 300 eventos, 0 pendentes, 0 publicados mais de uma vez, 300 linhas na Inbox. Divisão do trabalho na 1ª execução: publicados 150/102/48 e aplicados 97/101/102 entre as três réplicas, 0 duplicados. Log: [scale-workers.log](../../tests/chaos/results/scale-workers.log).

**Extrair um módulo** se justifica por outro motivo que não capacidade:

| Gatilho | Exemplo neste sistema |
|---|---|
| Ciclo de deploy independente | Consolidation muda toda semana (novos relatórios); Transactions é estável e auditado |
| Isolamento de falha de runtime | Um pico de leitura no `GET /daily-balances` disputa CPU e conexões com o `POST /transactions` no mesmo processo da API |
| Dono e time diferentes | Um time de relatórios assume a consolidação |
| Requisitos de dados diferentes | Consolidation precisa de outro banco (colunar, réplica de leitura) ou de outra retenção |
| Escala que réplicas não resolvem | O PostgreSQL compartilhado vira o gargalo de um dos módulos |

Enquanto nenhum gatilho existir, o monólito modular é mais barato de operar: um deploy, um pipeline, sem latência de rede entre módulos.

## Por que a extração aqui é mecânica
A fronteira que um microsserviço exige já está imposta no código e no banco, e testada:

| Garantia | Onde é imposta |
|---|---|
| Um módulo não referencia o interior do outro; só o contrato `*.IntegrationEvents` | `ModuleTests.Module_Should_NotReferenceAnotherModulesInternals` (arquitetura) |
| O contrato depende apenas de `Common.Application` | `ModuleTests.IntegrationEvents_Should_DependOnCommonApplicationOnly` |
| Cada módulo tem schema, `DbContext`, migrations e histórico de migrations próprios | `ModuleDatabaseTests`; `__ef_migrations_history` em cada schema |
| Nenhuma leitura ou escrita cruzada no banco, nem por engano | Roles `transactions_app`/`consolidation_app`; `DatabaseRoleTests` (`42501`) |
| Sem foreign key, join ou transação entre schemas | [design](../software/01-design-and-contracts.md#persistência) |
| A comunicação já é assíncrona e passa pelo broker | Outbox → `cashflow.transactions` → fila da consolidação |
| O contrato do evento é versionado e testado dos dois lados | `TransactionCreatedContractTests` (produtor e consumidor) e o [exemplo](../api/events/transactions.transaction-created.v1.json) |
| Cada lado declara a topologia de que precisa | O publisher declara o exchange; o consumidor declara, de forma idempotente, o exchange, a própria fila, o exchange de dead-letter, a DLQ e os bindings. Qualquer um dos dois sobe primeiro sem depender do outro |
| O rastreio atravessa processos | `CorrelationId` no evento, na propriedade AMQP e nos logs dos dois hosts |

Ou seja: hoje os módulos já conversam como se fossem serviços diferentes. Extrair muda **onde** o código roda, não **como** ele conversa.

## O que ainda junta os módulos
É só composição e infraestrutura, tudo em pontos conhecidos:

| Ponto | Arquivo |
|---|---|
| Lista de módulos dos hosts | [src/Hosting/CashFlow.Hosting/Modules.cs](../../src/Hosting/CashFlow.Hosting/Modules.cs) |
| Registro dos dois módulos na API | [src/Api/CashFlow.Api/Program.cs](../../src/Api/CashFlow.Api/Program.cs) (`AddTransactionsModule`, `AddConsolidationModule`) |
| Registro dos dois módulos no Worker | [src/Worker/CashFlow.Worker/WorkerComposition.cs](../../src/Worker/CashFlow.Worker/WorkerComposition.cs) |
| Migrations dos dois módulos no mesmo bootstrap | [src/Api/CashFlow.Api/DatabaseMigrations.cs](../../src/Api/CashFlow.Api/DatabaseMigrations.cs) |
| Bibliotecas compartilhadas | `src/Common/CashFlow.Common.*` (Result, mensageria, Outbox, decorators, correlação) |
| Um servidor PostgreSQL e um RabbitMQ | [docker-compose.yml](../../docker-compose.yml), [01-roles.sh](../../deploy/postgres/init/01-roles.sh) |
| Um documento OpenAPI | [docs/api/openapi.json](../api/openapi.json) |

## Procedimento — exemplo: extrair Consolidation
Consolidation é o candidato natural: é o lado de leitura (o dos 50 RPS) e o consumidor. Transactions sai pelo mesmo roteiro, espelhado. Cada fase é implantável sozinha e tem volta.

### Fase 1 — novo deployable, mesmos dados (sem migração de dados)
1. **Hosts próprios.** Criar `CashFlow.Consolidation.Api` (endpoints de `Consolidation.Presentation`) e `CashFlow.Consolidation.Worker` (o consumidor), ou um host só com os dois papéis. Cada um compõe apenas o próprio módulo: `AddConsolidationModule`/`AddConsolidationWorker` e uma lista de módulos com os assemblies de Consolidation.
2. **Tirar o módulo do monólito.** Remover Consolidation de `Hosting/Modules.cs`, do `Program.cs`, do `WorkerComposition` e do `DatabaseMigrations`. O teste `ModuleRegistry_Should_MatchTheModuleFoldersOnDisk` passa a valer por host; ajustá-lo para a lista de cada host.
3. **Migrations no dono.** O novo serviço roda o próprio `--migrate` com `ConsolidationDbContext`. O histórico já está em `consolidation.__ef_migrations_history`; nada é reaplicado.
4. **Mesmo banco, mesma role.** A connection string continua apontando para o mesmo servidor, schema `consolidation`, role `consolidation_app`. Zero cópia de dados.
5. **Mensageria intacta.** A fila `consolidation.transaction-created.v1`, a DLQ e o contrato não mudam. Durante a troca, para o Worker antigo antes de subir o novo: os eventos ficam retidos na fila e na Outbox (o mesmo comportamento provado pelo [worker-down](../../tests/chaos/worker-down.sh)). Rodar os dois ao mesmo tempo também é seguro, porque a Inbox deduplica.
6. **Roteamento.** Um proxy na frente (YARP, Nginx ou o gateway da nuvem) manda `/daily-balances/*` para o novo serviço e o resto para o monólito. O cliente não percebe.
7. **Contratos.** Dividir o snapshot OpenAPI por serviço; o `OpenApiContractTests` passa a existir em cada um.
8. **Verificação.** Rodar de novo `worker-down.sh`, `broker-down.sh`, a carga de 50 RPS contra o novo serviço e os testes de contrato.

**Volta:** rotear `/daily-balances/*` para o monólito e registrar o módulo de novo. Nenhum dado mudou de lugar.

### Fase 2 — banco próprio (só se um gatilho de dados pedir)
1. Criar o banco ou servidor novo e a role `consolidation_app` nele.
2. Copiar o schema: `pg_dump --schema=consolidation` → `pg_restore` no destino, com o Worker de Consolidation parado (os eventos esperam na fila).
3. Trocar a connection string do serviço e religá-lo; a fila drena e o saldo converge.
4. Remover o schema `consolidation` e a role do servidor antigo depois do período de observação.

Não há foreign key nem join para desfazer. A alternativa "reconstruir o saldo a partir dos eventos" só vale se os eventos forem retidos (log de eventos ou Outbox não expurgada); o RabbitMQ não guarda o histórico depois do ACK.

### Fase 3 — repositório e pacotes (só se o time ou o ciclo de release separar)
1. `CashFlow.Common.*` vira pacote NuGet interno versionado; cada serviço atualiza no próprio ritmo.
2. `CashFlow.Modules.Transactions.IntegrationEvents` vira o pacote do contrato. O teste do lado do consumidor continua lendo o exemplo publicado pelo produtor; mudança incompatível é `TransactionCreated.v2` publicado junto com o v1 até ninguém consumir o v1 (regra que já está no próprio contrato).
3. Pipelines e repositórios separados, se fizer sentido para os times.

### O que muda na operação depois de extrair
- Dois deployables a mais para monitorar; os `Meter` de cada módulo (`CashFlow.Transactions`, `CashFlow.Consolidation`, `CashFlow.Messaging`) já estão separados por nome e vão junto.
- Versionamento do contrato passa a ser coordenação entre times, não refatoração.
- Nenhuma transação distribuída é criada: a única escrita que envolve os dois lados já é a Outbox + Inbox.

## Demais itens do P2
| Item | O que já existe | Proposta | Quando |
|---|---|---|---|
| Escala horizontal | Worker medido com 3 réplicas (acima); API sem estado | Réplicas da API atrás de um balanceador; KEDA ou autoscaling pela profundidade da fila e pelo `cashflow.consolidation.lag` | Lag p95 acima da meta ou CPU da API saturada |
| HA do broker | Filas quorum (o tipo replicado do RabbitMQ), mensagens persistentes, confirms | Cluster de 3 nós ou serviço gerenciado; nenhuma mudança de código, só o host na configuração | Antes de produção |
| HA do banco | Uma connection string por módulo | PostgreSQL gerenciado com réplica e failover; apontar cada connection string para o endpoint de failover | Antes de produção; é a falha comum aceita no [ADR-001](../adr/ADR-001.md) |
| Autenticação | Endpoints públicos por decisão do MVP | JWT/OAuth2 no host da API e `.RequireAuthorization()` nos `IEndpoint`, com escopos por módulo (`transactions:write`, `balances:read`) | Antes de expor fora da rede |
| Replay da DLQ | Manual pela management UI | Ferramenta operacional que move mensagens da `.dlq` de volta para o exchange; seguro porque a Inbox deduplica | Quando a DLQ receber algo além de testes |
| Dashboards e alertas | `Meter` por módulo, logs JSON com `CorrelationId`, `/health/ready` | Exportador OpenTelemetry (OTLP) e painéis; alertas: `dead_lettered` > 0, `circuit_open` > 0, lag p95 acima da meta, `ready` 503 | Antes de produção |
| Custos | — | Estimar só depois de escolher nuvem, região, SLA e volume ([03-operations](03-operations-and-roadmap.md#custos)) | Na escolha de nuvem |
