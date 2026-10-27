using CashFlow.Api;
using CashFlow.Common.Application;
using CashFlow.Common.Infrastructure;
using CashFlow.Common.Infrastructure.Health;
using CashFlow.Common.Presentation.Correlation;
using CashFlow.Common.Presentation.Endpoints;
using CashFlow.Common.Presentation.ExceptionHandling;
using CashFlow.Hosting;
using CashFlow.Modules.Consolidation.Infrastructure;
using CashFlow.Modules.Transactions.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCommonInfrastructure();
builder.Services.AddApplication(Modules.ApplicationAssemblies);
builder.Services.AddEndpoints(Modules.PresentationAssemblies);

builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
// Binding failures (unreadable JSON, a malformed date) throw in every
// environment, not only in Development, so UnhandledExceptionHandler answers
// them with the same problem document and log line. Without this, Production
// returns a bare 400 with an empty body.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Each module registers itself here as it is created; the host is the only
// assembly that knows the full list. The API registers no broker: a POST
// writes the transaction and its outbox message and is done.
builder.Services.AddTransactionsModule(builder.Configuration);
builder.Services.AddConsolidationModule(builder.Configuration);

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
// Liveness runs no check (the process answers); readiness opens a connection
// per module with its own role. /health stays as the liveness the compose
// healthcheck and existing clients already call.
var live = new HealthCheckOptions { Predicate = _ => false };
app.MapHealthChecks("/health", live);
app.MapHealthChecks("/health/live", live);
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) });

// Every IEndpoint found in the presentation assemblies of Modules.cs.
app.MapEndpoints();

await app.RunAsync();

/// <summary>
/// Exposed so the integration test host can reference this entry point.
/// </summary>
public partial class Program;
