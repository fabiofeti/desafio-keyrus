using CashFlow.Common.Domain;
using CashFlow.Common.Presentation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
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
        logger.LogError(
            exception,
            "Unhandled exception outside the command/query pipeline for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        await ApiResults.Problem(Result.Failure(Unhandled)).ExecuteAsync(httpContext);

        return true;
    }
}
