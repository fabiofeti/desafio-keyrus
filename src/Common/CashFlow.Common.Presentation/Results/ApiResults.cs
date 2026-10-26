using CashFlow.Common.Domain;
using Microsoft.AspNetCore.Http;

namespace CashFlow.Common.Presentation.Results;

/// <summary>
/// The single place an <see cref="Error"/> becomes a status code.
/// An endpoint that reads <c>result.Error.Code</c> to decide a status is doing
/// this translation a second time, differently.
/// </summary>
public static class ApiResults
{
    public static IResult Problem(Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("A successful result cannot be turned into a problem.");
        }

        return Microsoft.AspNetCore.Http.Results.Problem(
            title: GetTitle(result.Error),
            detail: GetDetail(result.Error),
            type: GetType(result.Error.Type),
            statusCode: GetStatusCode(result.Error.Type),
            extensions: GetExtensions(result));

        static string GetTitle(Error error) => error.Type switch
        {
            ErrorType.Validation => "Bad Request",
            ErrorType.Problem => "Bad Request",
            ErrorType.NotFound => "Not Found",
            ErrorType.Conflict => "Conflict",
            _ => "Server Failure"
        };

        static string GetDetail(Error error) => error.Type switch
        {
            ErrorType.Validation or ErrorType.Problem or ErrorType.NotFound or ErrorType.Conflict =>
                error.Description,
            // An unexpected failure says nothing about itself: the detail is in
            // the log, where it cannot leak internals to a caller.
            _ => "An unexpected error occurred."
        };

        static string GetType(ErrorType errorType) => errorType switch
        {
            ErrorType.Validation or ErrorType.Problem => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            ErrorType.NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            ErrorType.Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        };

        static int GetStatusCode(ErrorType errorType) => errorType switch
        {
            ErrorType.Validation or ErrorType.Problem => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        // The code travels with the document because a status code is not
        // enough to act on: two conflicts on the same route mean different
        // things - a submitted attempt is the end of the story, a stale draft
        // is something the client can recover from by itself - and a client
        // that has to match on the prose to tell them apart is a client that
        // breaks when the prose improves.
        static Dictionary<string, object?> GetExtensions(Result result)
        {
            var extensions = new Dictionary<string, object?> { { "code", result.Error.Code } };

            if (result.Error is ValidationError validationError)
            {
                extensions.Add("errors", validationError.Errors);
            }

            return extensions;
        }
    }
}
