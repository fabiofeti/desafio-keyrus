using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Application.Telemetry;
using CashFlow.Common.Domain;
using CashFlow.Common.Presentation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CashFlow.Common.Presentation.ExceptionHandling;

/// <summary>
/// Catches what <c>ExceptionHandlingDecorator</c> cannot reach: an exception
/// thrown outside a command or query handler - model binding, middleware,
/// anything that runs before or after that decorator's place in the pipeline.
/// Registered with <c>AddExceptionHandler</c> and put to work by
/// <c>UseExceptionHandler()</c>, ahead of every other middleware, so nothing
/// downstream can leak a stack trace past it.
/// </summary>
/// <remarks>
/// This is the same policy the decorator already applies to a handler-side
/// exception - anything not modeled as a <see cref="Result"/> is
/// <c>Error.Unhandled</c>, a 500, with the detail left in the log - applied to
/// the half of the request the decorator never sees. It answers through
/// <see cref="ApiResults.Problem"/> rather than building a document by hand, so
/// the two paths can never drift into different shapes.
/// </remarks>
public sealed class UnhandledExceptionHandler(ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Error Unhandled = Error.Failure(
        "Error.Unhandled",
        "An unexpected error occurred while handling the request.");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // This runs outside the correlation middleware (it wraps everything), so
        // it reopens the request's scope: the error line keeps its CorrelationId.
        string correlationId = httpContext.RequestServices.GetRequiredService<CorrelationContext>().CorrelationId;
        using IDisposable? scope = logger.BeginScope(LogScopes.Correlation(correlationId));

        // Binding failures - a malformed date in the query, unreadable JSON, a
        // missing required parameter - are the client's error, not ours: a 400
        // in the same problem shape, logged as information, not as an error.
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogInformation(
                "Rejected request for {Method} {Path}: {Reason}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                badRequest.Message);

            await ApiResults.Problem(Result.Failure(Error.Problem("Request.Invalid", badRequest.Message)))
                .ExecuteAsync(httpContext);

            return true;
        }

        logger.LogError(
            exception,
            "Unhandled exception outside the command/query pipeline for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        await ApiResults.Problem(Result.Failure(Unhandled)).ExecuteAsync(httpContext);

        return true;
    }
}
