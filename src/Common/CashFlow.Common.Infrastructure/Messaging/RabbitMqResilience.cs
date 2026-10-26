using System.Net.Sockets;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using RabbitMQ.Client.Exceptions;

namespace CashFlow.Common.Infrastructure.Messaging;

/// <summary>
/// The only Polly pipelines in the solution, and both are about the broker.
/// Retrying a publish is safe because the consumer deduplicates by event id in
/// its inbox; that reasoning does NOT extend to saving a transaction, which is
/// never retried blindly.
/// </summary>
internal static class RabbitMqResilience
{
    private static readonly PredicateBuilder<object> Transient = new PredicateBuilder<object>()
        .Handle<BrokerUnreachableException>()
        .Handle<ConnectFailureException>()
        .Handle<AlreadyClosedException>()
        .Handle<OperationInterruptedException>()
        .Handle<PublishException>()
        .Handle<TimeoutRejectedException>()
        .Handle<SocketException>()
        .Handle<IOException>();

    /// <summary>
    /// Publish: a few quick retries with jitter, a circuit breaker so a dead
    /// broker is not hammered, and a per-attempt timeout (innermost). The breaker
    /// lives in the Worker only - the API never publishes.
    /// </summary>
    public static ResiliencePipeline CreatePublishPipeline() => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = Transient,
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(200)
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ShouldHandle = Transient,
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15)
        })
        .AddTimeout(TimeSpan.FromSeconds(5))
        .Build();

    /// <summary>
    /// Connect: keeps trying while the host runs (the broker may simply not be
    /// up yet), backing off to at most 30 seconds between attempts.
    /// </summary>
    public static ResiliencePipeline CreateConnectPipeline() => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = Transient,
            MaxRetryAttempts = int.MaxValue,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30)
        })
        .Build();
}
