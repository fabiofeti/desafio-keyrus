using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Common.Infrastructure;

public static class InfrastructureConfiguration
{
    /// <summary>
    /// What both hosts need: time and the correlation id. Messaging is not
    /// here on purpose - only the Worker talks to the broker (see
    /// <see cref="Messaging.MessagingConfiguration.AddMessaging"/>), which is part of why
    /// the API keeps accepting transactions while RabbitMQ is down.
    /// </summary>
    public static IServiceCollection AddCommonInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationIdProvider>(sp => sp.GetRequiredService<CorrelationContext>());

        return services;
    }
}
