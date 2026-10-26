# Templates dos hosts

## Api

`src/Api/CashFlow.Api/Program.cs`

```csharp
using CashFlow.Api;
using CashFlow.Common.Application;
using CashFlow.Common.Infrastructure;
using CashFlow.Common.Presentation.Correlation;
using CashFlow.Common.Presentation.Endpoints;
using CashFlow.Common.Presentation.ExceptionHandling;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCommonInfrastructure();
builder.Services.AddApplication(Modules.ApplicationAssemblies);
builder.Services.AddEndpoints(Modules.PresentationAssemblies);

builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Cada módulo se registra aqui, e o host é o único assembly que conhece a lista.
// A API não registra RabbitMQ: um POST grava lançamento + Outbox e termina.
// builder.Services.AddTransactionsModule(builder.Configuration);
// builder.Services.AddConsolidationModule(builder.Configuration);

WebApplication app = builder.Build();

// Modo de bootstrap: o serviço `migrator` do compose roda a mesma imagem com
// --migrate e credenciais administrativas; api e worker nunca migram.
if (args.Contains("--migrate"))
{
    await DatabaseMigrations.ApplyAsync(app.Services);
    return;
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.MapOpenApi();                 // /openapi/v1.json
app.MapHealthChecks("/health");
app.MapEndpoints();               // todos os IEndpoint achados nos assemblies de Modules.cs

await app.RunAsync();

/// <summary>Exposto para o WebApplicationFactory dos testes de integração.</summary>
public partial class Program;
```

`src/Hosting/CashFlow.Hosting/Modules.cs`

```csharp
using System.Reflection;

namespace CashFlow.Hosting;

/// <summary>
/// A lista de módulos, num lugar só. Um teste de arquitetura compara esta lista
/// com as pastas em src/Modules, então um módulo não fica sem registro.
/// O Worker usa esta mesma lista: os módulos não são duplicados por host.
/// </summary>
public static class Modules
{
    /// <summary>Assemblies varridos atrás de handlers e validators.</summary>
    public static readonly Assembly[] ApplicationAssemblies = [];

    /// <summary>Assemblies varridos atrás de IEndpoint e IIntegrationEventHandler.</summary>
    public static readonly Assembly[] PresentationAssemblies = [];
}
```

`Modules.cs` fica num projeto pequeno `src/Hosting/CashFlow.Hosting` (class library
que referencia os projetos de módulo) para que o Worker use a lista sem referenciar
o Api, que é Web SDK. Api e Worker referenciam `CashFlow.Hosting`. É o único arquivo
compartilhado entre os hosts, e o teste de registro olha para ele.

`DatabaseMigrations.ApplyAsync(IServiceProvider)`: abre um escopo e chama
`Database.MigrateAsync()` em cada DbContext de módulo, um por linha, na ordem
Transactions, Consolidation. Os schemas são independentes (sem FK entre eles),
então a ordem não importa.

## Worker

`src/Worker/CashFlow.Worker/Program.cs`

```csharp
using CashFlow.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWorker(builder.Configuration);

await builder.Build().RunAsync();
```

`src/Worker/CashFlow.Worker/WorkerComposition.cs`

```csharp
namespace CashFlow.Worker;

/// <summary>
/// Público para que os testes de integração montem exatamente o mesmo Worker.
/// </summary>
public static class WorkerComposition
{
    public static IServiceCollection AddWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCommonInfrastructure();
        services.AddMessaging(configuration);
        services.AddApplication(Modules.ApplicationAssemblies);
        services.AddIntegrationEventHandlers(Modules.PresentationAssemblies);

        // Cada módulo registra o próprio lado de background com a própria
        // connection string e a própria role:
        // services.AddTransactionsWorker(configuration);   // Outbox publisher
        // services.AddConsolidationWorker(configuration);  // consumer + Inbox

        return services;
    }
}
```

## Integration tests

`tests/CashFlow.IntegrationTests/IntegrationTestWebAppFactory.cs`, em resumo:

- `PostgreSqlBuilder().WithImage("postgres:17-alpine")` com
  `TRANSACTIONS_APP_PASSWORD`/`CONSOLIDATION_APP_PASSWORD` gerados no teste e
  `WithResourceMapping` de `deploy/postgres/init/` para `/docker-entrypoint-initdb.d/`.
  O teste usa **o mesmo script** do compose: o isolamento por role testado é o real.
- `RabbitMqBuilder().WithImage("rabbitmq:4-management-alpine")`.
- `InitializeAsync`: sobe os dois containers e aplica as migrations com a
  connection string administrativa (usando `{Módulo}Module.ConfigureDbContext`, o
  mesmo método que o módulo usa em runtime).
- `ConfigureWebHost`: `UseSetting("ConnectionStrings:Transactions", <role transactions_app>)`,
  idem Consolidation.
- Para cenários de Worker, um `IHost` montado com `WorkerComposition.AddWorker` e
  as mesmas configurações, iniciado e parado dentro do teste.
- Toda classe de teste: `[Trait("Category", "Integration")]`. O hook de Stop roda
  só `Category!=Integration`; `/verify` roda tudo.
