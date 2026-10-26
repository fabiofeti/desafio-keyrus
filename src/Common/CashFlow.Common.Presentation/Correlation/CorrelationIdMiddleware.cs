using System.Text.RegularExpressions;
using CashFlow.Common.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace CashFlow.Common.Presentation.Correlation;

/// <summary>
/// Reads <c>X-Correlation-Id</c> into the request's <see cref="CorrelationContext"/>
/// (or lets it mint one) and echoes it on the response. The same id is stored on
/// the outbox row and travels on the published message, so one request can be
/// followed from the POST to the consolidation.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        string? incoming = context.Request.Headers[HeaderName];

        // Anything else is ignored rather than trusted: the value ends up in logs
        // and in a database column.
        if (incoming is not null && AllowedValue().IsMatch(incoming))
        {
            correlation.Set(incoming);
        }

        string correlationId = correlation.CorrelationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,100}$")]
    private static partial Regex AllowedValue();
}
