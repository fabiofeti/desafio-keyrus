using CashFlow.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CashFlow.IntegrationTests;

/// <summary>
/// The real Worker composition (<see cref="WorkerComposition.AddWorker"/>) over
/// the test containers, with the restricted database roles. Started, it runs
/// the outbox publisher and the consumer; not started, its services can be
/// driven one call at a time.
/// </summary>
internal sealed class WorkerHost : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _started;

    private WorkerHost(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public static WorkerHost Create(
        IntegrationTestWebAppFactory factory, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>(factory.RabbitMqSettings)
        {
            ["ConnectionStrings:Transactions"] = factory.TransactionsConnectionString,
            ["ConnectionStrings:Consolidation"] = factory.ConsolidationConnectionString,
            ["Outbox:PollingInterval"] = "00:00:00.200",
            // Fewer returns before dead-lettering, so a poison message settles in
            // seconds. Every test host uses the same value: the limit is part of
            // the queue's declaration in the shared broker.
            ["RabbitMq:DeliveryLimit"] = "1",
            ["Logging:LogLevel:Default"] = "Warning"
        };

        foreach ((string key, string? value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddWorker(builder.Configuration);

        return new WorkerHost(builder.Build());
    }

    public static async Task<WorkerHost> StartAsync(IntegrationTestWebAppFactory factory)
    {
        WorkerHost worker = Create(factory);
        await worker._host.StartAsync();
        worker._started = true;
        return worker;
    }

    public async Task StopAsync()
    {
        if (_started)
        {
            await _host.StopAsync();
            _started = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _host.Dispose();
    }
}
