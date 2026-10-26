namespace CashFlow.Common.Infrastructure.Messaging;

/// <summary>
/// Broker settings. Credentials come from the environment (<c>RabbitMq__Username</c>,
/// <c>RabbitMq__Password</c>), never from an appsettings file.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string VirtualHost { get; init; } = "/";

    public ushort PrefetchCount { get; init; } = 16;

    public string ClientProvidedName { get; init; } = "cashflow-worker";
}
