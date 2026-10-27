using CashFlow.Common.Application.EventBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CashFlow.Common.Infrastructure.Messaging;

public static class MessagingConfiguration
{
    /// <summary>
    /// The broker connection and the confirmed publisher. Worker only.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Username) && !string.IsNullOrWhiteSpace(o.Password),
                "RabbitMq:Username and RabbitMq:Password are required (set RabbitMq__Username and RabbitMq__Password).")
            .ValidateOnStart();

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

        return services;
    }

    /// <summary>
    /// Starts a background consumer for one subscription. Called by the consuming
    /// module's worker registration.
    /// </summary>
    public static IServiceCollection AddIntegrationEventConsumer<TEvent>(
        this IServiceCollection services,
        SubscriptionOptions<TEvent> subscription)
        where TEvent : IIntegrationEvent
    {
        services.AddSingleton(subscription);
        services.TryAddSingleton<MessagingMetrics>();
        services.AddHostedService<RabbitMqConsumer<TEvent>>();

        return services;
    }
}
