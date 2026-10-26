using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CashFlow.Modules.Transactions.Infrastructure.Outbox;

/// <summary>
/// The Worker's loop around <see cref="OutboxProcessor"/>. Runs only in the
/// Worker: the API writes rows to the outbox and never talks to the broker.
/// </summary>
internal sealed class OutboxPublisher(
    OutboxProcessor processor,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool topologyDeclared = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            OutboxCycle cycle = default;

            try
            {
                if (!topologyDeclared)
                {
                    await processor.DeclareTopologyAsync(stoppingToken);
                    topologyDeclared = true;
                }

                cycle = await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Database or broker trouble outside a single publish: the rows
                // are still in the outbox, so the next cycle simply tries again.
                logger.LogWarning(exception, "Outbox cycle failed; retrying after {Interval}", options.Value.PollingInterval);
                cycle = new OutboxCycle(0, 0, Failed: true);
            }

            // A full, healthy batch means there may be more: go again at once.
            bool keepDraining = !cycle.Failed && cycle.Locked == options.Value.BatchSize;
            if (keepDraining)
            {
                continue;
            }

            try
            {
                await Task.Delay(options.Value.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
