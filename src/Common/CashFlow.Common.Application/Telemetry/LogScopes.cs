using System.Collections;

namespace CashFlow.Common.Application.Telemetry;

/// <summary>
/// The log scopes every entry point opens, so a JSON log line carries the ids
/// that tie it to a request or a message. Same keys on the API and the Worker:
/// one query on <c>CorrelationId</c> follows a POST through the outbox, the
/// broker and the consolidation.
/// </summary>
public static class LogScopes
{
    public const string CorrelationId = nameof(CorrelationId);
    public const string MessageId = nameof(MessageId);
    public const string MessageType = nameof(MessageType);

    public static IReadOnlyList<KeyValuePair<string, object?>> Correlation(string correlationId) =>
        new Scope([new(CorrelationId, correlationId)]);

    public static IReadOnlyList<KeyValuePair<string, object?>> Message(string correlationId, string messageId, string messageType) =>
        new Scope([new(CorrelationId, correlationId), new(MessageId, messageId), new(MessageType, messageType)]);

    /// <summary>
    /// Key/value pairs become fields in the JSON formatter; <see cref="ToString"/>
    /// is the scope's "Message", written the way ASP.NET Core writes its own.
    /// </summary>
    private sealed class Scope(KeyValuePair<string, object?>[] values) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => values.Length;

        public KeyValuePair<string, object?> this[int index] => values[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => string.Join(" ", values.Select(v => $"{v.Key}:{v.Value}"));
    }
}
