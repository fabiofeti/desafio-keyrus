using System.Reflection;
using CashFlow.Common.Application.Decorators;
using CashFlow.Common.Application.EventBus;
using CashFlow.Common.Application.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Common.Application;

public static class ApplicationConfiguration
{
    /// <summary>
    /// Registers every handler and validator found in the given module
    /// assemblies, then wraps the handlers in the decorators. Adding a use case
    /// never touches this file - if it does, the scanning is wrong.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        params Assembly[] moduleApplicationAssemblies)
    {
        // No modules yet. Scanning an empty set is not an error worth throwing
        // over - the hosts are meant to boot before the first module exists.
        if (moduleApplicationAssemblies.Length == 0)
        {
            AddDecorators(services);

            return services;
        }

        services.Scan(scan => scan.FromAssemblies(moduleApplicationAssemblies)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        services.AddValidatorsFromAssemblies(moduleApplicationAssemblies, includeInternalTypes: true);

        AddDecorators(services);

        return services;
    }

    /// <summary>
    /// Registers the integration event handlers found in the given module
    /// presentation assemblies. Separate from <see cref="AddApplication"/>
    /// because these live one layer out: hearing another module's event is a
    /// way in from outside, like an endpoint, and the assembly that holds it is
    /// the only one allowed to name a neighbour's contract.
    /// </summary>
    /// <remarks>
    /// Only the Worker calls this. Without it the broker consumer resolves no
    /// handler for a message type - registration is what makes a subscription real.
    /// </remarks>
    public static IServiceCollection AddIntegrationEventHandlers(
        this IServiceCollection services,
        params Assembly[] modulePresentationAssemblies)
    {
        if (modulePresentationAssemblies.Length == 0)
        {
            return services;
        }

        services.Scan(scan => scan.FromAssemblies(modulePresentationAssemblies)
            .AddClasses(c => c.AssignableTo(typeof(IIntegrationEventHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        return services;
    }

    /// <summary>
    /// Order matters and is the reverse of the call order: the last decorator
    /// applied is the outermost. This produces exception handling → logging →
    /// validation → handler, from outermost to innermost.
    /// </summary>
    private static void AddDecorators(IServiceCollection services)
    {
        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandHandler<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));

        services.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandHandler<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));

        services.TryDecorate(typeof(ICommandHandler<>), typeof(ExceptionHandlingDecorator.CommandHandler<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ExceptionHandlingDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(ExceptionHandlingDecorator.QueryHandler<,>));
    }
}
