using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;

namespace CashFlow.Common.Infrastructure.Messaging;

/// <summary>
/// One broker connection per process, created lazily and replaced when it
/// closes. Automatic recovery is off: the publisher and every consumer already
/// rebuild their channels in their own loops, and two recovery mechanisms
/// fighting over one connection is harder to reason about than one.
/// </summary>
public sealed class RabbitMqConnectionProvider(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnectionProvider> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ResiliencePipeline _connect = RabbitMqResilience.CreateConnectPipeline();
    private IConnection? _connection;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            RabbitMqOptions settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.Username,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                ClientProvidedName = settings.ClientProvidedName,
                AutomaticRecoveryEnabled = false
            };

            _connection = await _connect.ExecuteAsync(
                async token =>
                {
                    logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port}", settings.Host, settings.Port);
                    return await factory.CreateConnectionAsync(token);
                },
                cancellationToken);

            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
