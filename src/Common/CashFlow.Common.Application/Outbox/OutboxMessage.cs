using System.Text.Json;
using CashFlow.Common.Application.EventBus;

namespace CashFlow.Common.Application.Outbox;

/// <summary>
/// An integration event that has not reached the broker yet. Written by the
/// handler in the same <c>SaveChangesAsync</c> as the fact it describes, and
/// marked as published only after the broker confirmed it.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the event's own id, not a delivery id. A message can be
/// published more than once - a lost confirm, a retry, a worker dying between
/// the confirm and the commit - and the consumer deduplicates on this id in its
/// inbox. That is why it is born on the event and copied here.
/// <para>
/// <see cref="Type"/> is a versioned string literal owned by the contract (for
/// example <c>transactions.transaction-created.v1</c>), never a CLR type name: a
/// queued row outlives a rename.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>
    /// One shared, read-only dialect for every payload, so it cannot drift with
    /// some caller's ambient options - a row outlives the deployment that wrote it.
    /// </summary>
    public static readonly JsonSerializerOptions PayloadSerializerOptions = CreateOptions();

    private const int MaxErrorLength = 2000;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private init; }

    public string Type { get; private init; } = string.Empty;

    public string Payload { get; private init; } = string.Empty;

    public string CorrelationId { get; private init; } = string.Empty;

    public DateTime OccurredOnUtc { get; private init; }

    public DateTime? PublishedOnUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage From<TEvent>(TEvent integrationEvent, string type, string correlationId)
        where TEvent : IIntegrationEvent =>
        new()
        {
            Id = integrationEvent.Id,
            Type = type,
            Payload = JsonSerializer.Serialize(integrationEvent, PayloadSerializerOptions),
            CorrelationId = correlationId,
            OccurredOnUtc = integrationEvent.OccurredOnUtc
        };

    /// <summary>Called only after the broker acknowledged the publish.</summary>
    public void MarkPublished(DateTime nowUtc)
    {
        AttemptCount++;
        PublishedOnUtc = nowUtc;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        AttemptCount++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Explicit reflection-based fallback: options without a resolver throw
        // on MakeReadOnly() otherwise. Payload types are plain records.
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
