---
name: add-module
description: Cria um bounded context no monólito modular CashFlow - projetos Domain, Application, Infrastructure, Presentation e IntegrationEvents, schema e role PostgreSQL próprios, DbContext com connection string própria, registro na API, no Worker e nos testes de arquitetura. Use quando o usuário pedir para criar o módulo Transactions ou Consolidation, ou outro bounded context.
argument-hint: <nome do módulo, ex.: "Transactions" ou "Consolidation">
---

# Adicionar um módulo

Um módulo é um bounded context com projetos próprios, schema próprio, **role de
banco própria** e nenhuma referência vinda de outro módulo. O desafio tem dois:
**Transactions** e **Consolidation**. Um terceiro precisa de motivo dito em voz
alta e aprovado pelo usuário.

Pré-requisito: o `scaffold-solution` já rodou (`CashFlow.slnx` existe).

## Projetos

Em `src/Modules/{Module}/`:

| Projeto | Contém | Referencia |
|---|---|---|
| `CashFlow.Modules.{Module}.Domain` | Agregados, value objects, `{Entity}Errors` | `Common.Domain` |
| `….Application` | Commands, queries, handlers, validators, `I{Module}DbContext`, `I{Module}UnitOfWork`, portas | Domain, `Common.Application`, o próprio `IntegrationEvents`, `Microsoft.EntityFrameworkCore` (só abstrações) |
| `….Infrastructure` | DbContext, configurações EF, migrations, SQL específico, `{Module}Module` (registro), serviços de background do módulo | Application, `Common.Infrastructure`, Npgsql EF, `EFCore.NamingConventions` |
| `….Presentation` | `IEndpoint`s, `IIntegrationEventHandler<T>`s, marcador `{Module}Presentation` | Application, `Common.Presentation` |
| `….IntegrationEvents` | Contratos públicos versionados do módulo | `Common.Application` apenas |

Consolidation pode referenciar `CashFlow.Modules.Transactions.IntegrationEvents`
(em Application, Infrastructure e Presentation) — é a única referência entre
módulos permitida. O hook `guard-module-boundary.sh` bloqueia qualquer outra.

## Passos

1. **Criar os cinco projetos** e adicioná-los ao `CashFlow.slnx` na pasta
   `/src/modules/{module}/`. Sem `TargetFramework`/`Nullable` no csproj (vêm do
   `Directory.Build.props`) e sem versão de pacote (vem do `Directory.Packages.props`).
   `InternalsVisibleTo` de Application e Infrastructure para `CashFlow.Modules.{Module}.UnitTests`
   e `CashFlow.IntegrationTests`.
2. **Schema** em `Infrastructure/Database/Schemas.cs`, `internal const string {Module} = "{module}"`
   (minúsculo: `transactions`, `consolidation`). Precisa bater com o
   `deploy/postgres/init/01-roles.sh`; se o módulo for novo, acrescente lá o schema,
   a role `{module}_app` e os grants (o script é o dono das roles).
3. **DbContext** `public sealed class {Module}DbContext : DbContext, I{Module}DbContext, I{Module}UnitOfWork`
   com `HasDefaultSchema(Schemas.{Module})` e `ApplyConfigurationsFromAssembly`.
   Transactions também aplica `OutboxMessageConfiguration`.
4. **Registro** — `{Module}Module` em Infrastructure:

   ```csharp
   public static class TransactionsModule
   {
       public const string ConnectionStringName = "Transactions";

       /// <summary>API: DbContext e portas. Nada de broker.</summary>
       public static IServiceCollection AddTransactionsModule(this IServiceCollection services, IConfiguration configuration)
       {
           string connectionString = configuration.GetConnectionString(ConnectionStringName)
               ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

           services.AddDbContext<TransactionsDbContext>(options => ConfigureDbContext(options, connectionString));
           services.AddScoped<ITransactionsDbContext>(sp => sp.GetRequiredService<TransactionsDbContext>());
           services.AddScoped<ITransactionsUnitOfWork>(sp => sp.GetRequiredService<TransactionsDbContext>());

           return services;
       }

       /// <summary>Worker: o mesmo módulo, mais o que roda em background.</summary>
       public static IServiceCollection AddTransactionsWorker(this IServiceCollection services, IConfiguration configuration)
       {
           services.AddTransactionsModule(configuration);
           // services.AddHostedService<OutboxPublisher>();   // add-integration-event
           return services;
       }

       /// <summary>Público para que migração e testes usem exatamente as mesmas opções.</summary>
       public static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) => options
           .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Transactions))
           .UseSnakeCaseNamingConvention();
   }
   ```

   Cada módulo lê **a sua** connection string (`ConnectionStrings:Transactions`,
   `ConnectionStrings:Consolidation`). Nunca uma `ConnectionStrings:Database`
   compartilhada: é ela que garante, em runtime, que o Worker hospedando os dois
   módulos continua sem escrita cruzada.
   `I{Module}UnitOfWork : IUnitOfWork` existe pelo mesmo motivo do CoreMastery: um
   `IUnitOfWork` global registrado duas vezes faz o segundo vencer em silêncio.
5. **Hosts**:
   - `src/Hosting/CashFlow.Hosting/Modules.cs`: um assembly de Application e um de
     Presentation do módulo (por um tipo marcador, ex.: `typeof(TransactionsPresentation).Assembly`).
   - `src/Api/CashFlow.Api/Program.cs`: `builder.Services.Add{Module}Module(builder.Configuration);`
     e `DatabaseMigrations.cs`: o `MigrateAsync` do DbContext novo.
   - `src/Worker/CashFlow.Worker/WorkerComposition.cs`: `services.Add{Module}Worker(configuration);`
   - csproj do `CashFlow.Hosting`: referência a Application, Infrastructure e Presentation do módulo.
6. **Testes de arquitetura**: entrada em `tests/CashFlow.ArchitectureTests/Assemblies.cs`.
   `ModuleRegistry_Should_MatchTheModuleFoldersOnDisk` falha se pasta, `Assemblies.cs`
   e `Modules.cs` discordarem — não contorne o teste, ele é o ponto.
7. **Testes do módulo**: `tests/CashFlow.Modules.{Module}.UnitTests` (xUnit, Shouldly,
   NSubstitute, Sqlite em memória para handlers que usam o DbContext) e acréscimo
   no `IntegrationTestWebAppFactory` (connection string da role `{module}_app`).
8. **Migration** só quando houver a primeira entidade (`add-entity`).
9. **Compose**: acrescentar `ConnectionStrings__{Module}` em `migrator` (admin),
   `api` e `worker` (role restrita).
10. **Diagrama**: atualizar [docs/diagrams/architecture.md](../../../docs/diagrams/architecture.md)
    se o módulo não estiver lá.
11. **Verificar**: `dotnet build CashFlow.slnx` e `dotnet test CashFlow.slnx`.

## Regras

- **Sem referência de projeto entre módulos**, exceto para `…IntegrationEvents`.
- **Sem join, FK ou SQL entre schemas.** SQL escrito à mão só cita o próprio schema.
- **Sem chamada síncrona entre módulos.** Transactions não sabe que Consolidation existe.
- **A Presentation não referencia Infrastructure nem Domain** do próprio módulo
  (regra do CoreMastery, verificada em `ModuleLayers_Should_ObeyTheDependencyRule`).
- Crie o formato, não invente use cases. As fatias vêm depois, com `add-feature`.
