using CashFlow.Api;
using CashFlow.Common.Application;
using CashFlow.Common.Infrastructure;
using CashFlow.Common.Presentation.Correlation;
using CashFlow.Common.Presentation.Endpoints;
using CashFlow.Common.Presentation.ExceptionHandling;
using CashFlow.Hosting;
using CashFlow.Modules.Transactions.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCommonInfrastructure();
builder.Services.AddApplication(Modules.ApplicationAssemblies);
builder.Services.AddEndpoints(Modules.PresentationAssemblies);

builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Each module registers itself here as it is created; the host is the only
// assembly that knows the full list. The API registers no broker: a POST
// writes the transaction and its outbox message and is done.
builder.Services.AddTransactionsModule(builder.Configuration);

WebApplication app = builder.Build();

// Bootstrap mode: the compose `migrator` service runs this same image with
// --migrate and administrative credentials. The api and worker never migrate.
if (args.Contains("--migrate"))
{
    await DatabaseMigrations.ApplyAsync(app.Services);
    return;
}

// First, so it wraps everything downstream, model binding included.
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.MapOpenApi();
app.MapHealthChecks("/health");

// Every IEndpoint found in the presentation assemblies of Modules.cs.
app.MapEndpoints();

await app.RunAsync();

/// <summary>
/// Exposed so the integration test host can reference this entry point.
/// </summary>
public partial class Program;
