---
name: scaffold-solution
description: Etapa (1) do desafio Keyrus - cria a solução .NET 10 (CashFlow.slnx), os quatro projetos Common.*, os hosts CashFlow.Api e CashFlow.Worker, os testes de arquitetura, Dockerfiles, docker-compose com PostgreSQL 17 Alpine e RabbitMQ management, e o script de roles/schemas. Use uma única vez, quando o repositório ainda não tem CashFlow.slnx, ou quando o usuário pedir para criar/montar a solução, o compose ou a infraestrutura local.
argument-hint: (sem argumentos)
---

# Scaffold da solução

Cria o esqueleto que todos os outros skills assumem. Não cria módulos (isso é o
`add-module`) e não cria use cases. Ao final, `dotnet build` e `dotnet test` passam
com zero módulos e `docker compose config` valida.

Se `CashFlow.slnx` já existe, pare: o scaffold já foi feito. Use `add-module`.

## Fonte do padrão

O padrão vem de `/home/fabiofeti/projects/CoreMastery.SystemDesignPlatform/services`.
Copie de lá, troque `CoreMastery.SystemDesign` por `CashFlow` nos namespaces e
nomes de projeto, e **remova o que este desafio não usa** (tabela abaixo). Se o
caminho não existir, use os templates em [references/common.md](references/common.md).

| Arquivo de origem (`services/api/src/Common/…`) | Destino | Ajuste |
|---|---|---|
| `Common.Domain/{Result,Error,ValidationError}.cs` | `src/Common/CashFlow.Common.Domain/` | Só namespace |
| `Common.Domain/Entity.cs` | idem | Remover a coleção de domain events (não há dispatcher aqui) |
| `Common.Domain/IDomainEvent.cs` | — | Não copiar |
| `Common.Application/Messaging/*` | `src/Common/CashFlow.Common.Application/Messaging/` | Só namespace |
| `Common.Application/Decorators/{Validation,Logging,ExceptionHandling}Decorator.cs` | `…/Decorators/` | Sem `TracingDecorator` |
| `Common.Application/ApplicationConfiguration.cs` | `…/` | Tirar `IDomainEventHandler` e o tracing; manter `AddIntegrationEventHandlers` |
| `Common.Application/Abstractions/{IDateTimeProvider,IUnitOfWork,ICorrelationIdProvider}.cs` | `…/Abstractions/` | Sem `IUserContext` |
| `Common.Application/EventBus/{IIntegrationEvent,IIntegrationEventHandler}.cs` | `…/EventBus/` | Sem `IEventBus` em processo |
| `Common.Application/Outbox/OutboxMessage.cs` | `…/Outbox/` | Trocar pelo modelo de [references/common.md](references/common.md#outboxmessage) |
| `Common.Infrastructure/Time/DateTimeProvider.cs`, `Telemetry/CorrelationIdProvider.cs` | `src/Common/CashFlow.Common.Infrastructure/` | Correlation id lido do header `X-Correlation-Id` ou gerado |
| `Common.Presentation/Endpoints/*`, `Results/ApiResults.cs`, `ExceptionHandling/UnhandledExceptionHandler.cs` | `src/Common/CashFlow.Common.Presentation/` | Só namespace |
| `Directory.Build.props`, `Directory.Packages.props` | raiz | Ver lista de pacotes abaixo |
| `api/tests/…ArchitectureTests/{LayerTests,ModuleTests,Assemblies}.cs` | `tests/CashFlow.ArchitectureTests/` | Sem `AiDependencyTests`, `RealmTests`; `WorkerTests` reescrito (ver abaixo) |

Não copie: autenticação/JWT, Keycloak, OpenTelemetry, rate limiting, HybridCache,
`InProcessEventBus`, `DomainEventDispatchInterceptor`, nada de IA.

## Passos

1. **`global.json`** fixando o SDK 10.0.400 com `rollForward: latestFeature`.
2. **`Directory.Build.props`** (net10.0, `Nullable`, `ImplicitUsings`, `LangVersion latest`,
   `EnforceCodeStyleInBuild`) e **`Directory.Packages.props`** com gerenciamento central.
   Pacotes: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`, `Scrutor`,
   `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`,
   `Microsoft.Extensions.Hosting`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore`,
   `Microsoft.EntityFrameworkCore.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`,
   `EFCore.NamingConventions`, `RabbitMQ.Client` (7.x, API assíncrona), `Polly.Core` (8.x),
   `AspNetCore.HealthChecks.NpgSql` é opcional (P1). Testes: `xunit`, `xunit.runner.visualstudio`,
   `Microsoft.NET.Test.Sdk`, `Shouldly`, `NSubstitute`, `NetArchTest.Rules`,
   `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Sqlite`,
   `Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `coverlet.collector`.
   Use as versões estáveis mais recentes que restaurarem para net10.0; confirme com
   `dotnet restore`, não por memória.
3. **Os quatro `Common.*`**, com as referências: Domain → nada; Application → Domain;
   Infrastructure → Application; Presentation → Application (+ `FrameworkReference Microsoft.AspNetCore.App`).
   Em Infrastructure, além do relógio e correlation id, a camada de mensageria:
   `RabbitMqOptions`, `RabbitMqConnectionProvider`, `RabbitMqPublisher` (confirms + Polly) e
   `RabbitMqConsumer<TEvent>` (hosted service). Templates em
   [references/messaging.md](references/messaging.md). O `add-integration-event` usa essa base.
4. **`src/Hosting/CashFlow.Hosting`** (class library): só `Modules.cs`, a lista de
   assemblies de módulo que os dois hosts usam. Começa com as listas vazias; o
   `add-module` acrescenta. **`src/Api/CashFlow.Api`** (`Microsoft.NET.Sdk.Web`):
   `Program.cs` e `DatabaseMigrations.cs`, referenciando `CashFlow.Hosting`.
   Template em [references/hosts.md](references/hosts.md#api).
   O Api aceita o argumento `--migrate`: aplica as migrations de cada módulo com as
   connection strings de migração e termina. Sem esse argumento, nunca migra.
5. **`src/Worker/CashFlow.Worker`** (`Microsoft.NET.Sdk.Worker`): `Program.cs` com
   `Host.CreateApplicationBuilder`, `AddApplication(Modules.ApplicationAssemblies)`,
   `AddIntegrationEventHandlers(Modules.PresentationAssemblies)`, e o `Add{Módulo}Worker`
   de cada módulo. Template em [references/hosts.md](references/hosts.md#worker).
   **Não** mapeia endpoints (verificado em `HostTests.Worker_Should_NotMapEndpoints`). Ele recebe
   o shared framework do ASP.NET Core de forma transitiva, porque os `IIntegrationEventHandler`
   ficam na Presentation dos módulos; por isso a imagem final do Worker é `aspnet`, não `runtime`.
6. **`tests/CashFlow.ArchitectureTests`**: `LayerTests` e `ModuleTests` adaptados, e
   `HostTests` com duas regras: o Worker não referencia nenhum `*.Presentation` para
   mapear endpoint (só para handlers de integração) e nenhum `Common.*` referencia `CashFlow.Modules.*`.
   `ModuleRegistry_Should_MatchTheModuleFoldersOnDisk` procura a raiz pelo arquivo
   `CashFlow.slnx` e as pastas em `src/Modules`. Com zero módulos, passa.
7. **`tests/CashFlow.IntegrationTests`** com `IntegrationTestWebAppFactory`
   (Testcontainers PostgreSQL 17 Alpine + RabbitMQ 4 management), que executa
   `deploy/postgres/init/01-roles.sh` no container, aplica as migrations e aponta cada
   módulo para a role restrita. Base classe `BaseIntegrationTest`, `[Trait("Category","Integration")]`
   em toda classe (o hook de Stop exclui essa categoria). Template em
   [references/hosts.md](references/hosts.md#integration-tests).
8. **Infra local**: `deploy/postgres/init/01-roles.sh`, `src/Api/Dockerfile`,
   `src/Worker/Dockerfile`, `docker-compose.yml`, `.env.example`, `.gitignore`,
   `.dockerignore`. Templates em [references/compose.md](references/compose.md).
9. **Verificar**: `dotnet build CashFlow.slnx`, `dotnet test CashFlow.slnx`,
   `docker compose config -q`. Ainda sem módulo, `docker compose up` não é exigido nesta etapa.
10. Se o repositório não for git, sugira `git init` ao usuário (não rode sem pedir).

## Regras

- **Nenhum segredo no Git.** Compose lê tudo de `.env` (ignorado); `.env.example`
  só tem placeholders. `appsettings*.json` não tem senha. O hook `guard-secrets.sh` bloqueia.
- **Duas famílias de credencial**: `*_MIGRATOR` (admin, usada só pelo serviço `migrator`
  do compose) e `*_APP` (roles restritas, usadas por api e worker).
- **Um assembly de host não contém regra de negócio.** Api e Worker só compõem.
- Não adicionar autenticação, telemetria, cache ou frontend nesta etapa.
