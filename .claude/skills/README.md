# Skills do Keyrus Cash Flow

As convenções deste repositório em forma executável. Foram adaptadas dos skills
do `CoreMastery.SystemDesignPlatform` (que por sua vez vêm do padrão de monólito
modular do projeto `evently`) e recortadas para o que este desafio é: **dois
módulos** (Transactions e Consolidation), **dois hosts** (API e Worker),
**Outbox → RabbitMQ → Inbox** e nada além do que o desafio pede.

Leia antes: [CLAUDE.md](../../CLAUDE.md), [IMPLEMENTATION-PLAN](../../docs/IMPLEMENTATION-PLAN.md),
[ADR-001](../../docs/adr/ADR-001.md), [design e contratos](../../docs/software/01-design-and-contracts.md).

| Skill | Como invocar | O que faz |
|---|---|---|
| **scaffold-solution** | `/scaffold-solution` | Etapa (1): solução, `Common.*`, hosts API/Worker, testes de arquitetura, Dockerfiles, Compose, roles do PostgreSQL. Roda uma vez. |
| **add-module** | `/add-module Consolidation` | Um bounded context: cinco projetos, schema e role próprios, registro no host e nos testes de arquitetura. |
| **add-entity** | `/add-entity Transaction com tipo, valor e data de negócio` | Um agregado: entidade, catálogo de erros, configuração EF, DbContext e migration. |
| **add-feature** | `/add-feature criar lançamento` | Uma fatia vertical: command/query, handler, validator, endpoint na Presentation do módulo e os testes. |
| **add-integration-event** | `/add-integration-event TransactionCreated.v1` | Contrato versionado, escrita na Outbox, publicação com confirms + Polly, consumo com Inbox atômica. |
| **add-challenge** | `/add-challenge P0 GET /daily-balances` | Leva **um item do desafio** (P0/P1 do plano) pelos quatro gates até ter evidência real e documentação atualizada. |
| **ca-review** | `/ca-review` | Revisa as mudanças contra as regras inegociáveis e estas convenções. Só aponta, não corrige. |

Comandos (`.claude/commands`): `/verify` (build + testes + compose config) e
`/next-item` (próximo item pendente do plano, na ordem P0 → P1).

## Onde o código fica

```text
CashFlow.slnx                       Uma solução, raiz do repositório
Directory.Build.props               net10.0, nullable, implicit usings
Directory.Packages.props            Versões centralizadas
src/
  Hosting/CashFlow.Hosting          Modules.cs: a lista de assemblies de módulo, usada pelos dois hosts
  Api/CashFlow.Api                  Host HTTP: compõe módulos, mapeia IEndpoint por scanning
  Worker/CashFlow.Worker            Host de background: publisher da Outbox + consumidor RabbitMQ
  Common/
    CashFlow.Common.Domain          Entity, Result, Error, ValidationError
    CashFlow.Common.Application     ICommand/IQuery, decorators (Scrutor), IIntegrationEvent, OutboxMessage
    CashFlow.Common.Infrastructure  Relógio, correlation id, RabbitMQ (conexão, publisher, consumer), Polly
    CashFlow.Common.Presentation    IEndpoint + AddEndpoints/MapEndpoints, ApiResults
  Modules/
    Transactions/CashFlow.Modules.Transactions.{Domain,Application,Infrastructure,Presentation,IntegrationEvents}
    Consolidation/CashFlow.Modules.Consolidation.{Domain,Application,Infrastructure,Presentation,IntegrationEvents}
tests/
  CashFlow.ArchitectureTests        NetArchTest: camadas e fronteira entre módulos
  CashFlow.Modules.Transactions.UnitTests
  CashFlow.Modules.Consolidation.UnitTests
  CashFlow.IntegrationTests         Testcontainers PostgreSQL + RabbitMQ, [Trait("Category","Integration")]
  load/                             Script k6 dos 50 RPS
deploy/postgres/                    Script de init: schemas, roles e grants
docker-compose.yml, .env.example
```

## Como a API carrega os módulos

Uma única API. Nenhum endpoint é mapeado no `Program.cs`: cada endpoint é uma
classe `IEndpoint` dentro de `…{Módulo}.Presentation/{Feature}/{UseCase}.cs`.
Na inicialização o host percorre os assemblies listados em `src/Hosting/CashFlow.Hosting/Modules.cs`,
registra cada `IEndpoint` no DI (`AddEndpoints`) e chama `MapEndpoint` em todos
(`MapEndpoints`). Handlers e validators são achados do mesmo jeito (`AddApplication`
com Scrutor). Criar uma feature nunca edita arquivo compartilhado.

O Worker usa os **mesmos assemblies de módulo**, sem duplicar domínio. Ele
registra handlers e os `IIntegrationEventHandler<T>` (que ficam na Presentation
do módulo consumidor, como no CoreMastery), mas não mapeia endpoints.

## O que estes skills sabem e um assistente genérico não

1. **Um módulo nunca grava nas tabelas do outro nem referencia o DbContext dele.**
   A única referência permitida entre módulos é ao assembly `…IntegrationEvents`.
   Cada DbContext usa a própria connection string, com a própria role
   (`transactions_app`, `consolidation_app`). O Worker hospeda os dois módulos e,
   por isso mesmo, usa duas connection strings, nunca uma role com acesso a tudo.
2. **Lançamento e Outbox na mesma transação local.** O handler adiciona o
   lançamento e a `OutboxMessage` no mesmo `SaveChangesAsync`. Nunca publica no
   broker de dentro do request.
3. **Publicação só é marcada após o confirm do broker.** Publisher confirms ligados,
   mensagem persistente, `published_at` gravado depois do ack.
4. **Inbox e saldo na mesma instrução.** `INSERT … ON CONFLICT DO NOTHING` no
   inbox e o `UPSERT` incremental do saldo na mesma transação, e ACK só depois do
   commit. Redelivery não duplica valor. (O handler idempotente do evently faz
   check e insert em conexões separadas; aqui isso não serve.)
5. **Falha da consolidação não derruba o POST.** Nenhuma chamada síncrona de
   Transactions para Consolidation, e nenhum circuit breaker no caminho do POST.
6. **Polly só onde a falha é transitória e a operação é idempotente**: conexão e
   publicação no RabbitMQ. Nunca retry cego em `SaveChanges` de lançamento.
7. **Nada de "50 RPS atendido" sem teste executado.** O resultado vai para a
   documentação com comando, ambiente e números reais.

## Diferenças deliberadas em relação ao CoreMastery

| CoreMastery | Aqui | Por quê |
|---|---|---|
| JWT/Keycloak, `IUserContext`, `.RequireAuthorization()` | Sem autenticação no MVP; endpoints públicos documentados como tal | `CLAUDE.md`: sem autenticação corporativa; JWT fica como evolução |
| Domain events + `DomainEventDispatchInterceptor` | Sem dispatcher de domain events | O único fato entre módulos é a integração `TransactionCreated.v1`, escrita explicitamente na Outbox |
| Event bus em processo | RabbitMQ entre Worker e módulos | É o requisito central do desafio |
| Outbox com lease/claim | `FOR UPDATE SKIP LOCKED` + `published_at` + `attempt_count` | Mais simples e suficiente para um publisher |
| OpenTelemetry, rate limiting, HybridCache | Fora (P1/P2) | Não expandir escopo |
| `Shared.Contracts` (proposta do doc de design) | `CashFlow.Modules.Transactions.IntegrationEvents` | Mesmo papel (contrato público e versionado), no padrão do monólito modular. Atualizar o doc de design ao implementar |

## Personalizando

Cada skill é Markdown (`SKILL.md`, e templates em `references/`). Mude a convenção
uma vez aqui e todas as próximas features seguem.
