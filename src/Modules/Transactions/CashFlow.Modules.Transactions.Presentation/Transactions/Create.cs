using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Common.Presentation.Endpoints;
using CashFlow.Common.Presentation.Results;
using CashFlow.Modules.Transactions.Application.Transactions.Create;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CashFlow.Modules.Transactions.Presentation.Transactions;

internal sealed class Create : IEndpoint
{
    // The wire format, separate from the command: the command does not learn HTTP.
    internal sealed record Request(string Type, decimal Amount, DateTimeOffset OccurredAt, string? Description);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("transactions", async (
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            Request request,
            ICommandHandler<CreateTransactionCommand, CreatedTransaction> handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateTransactionCommand(
                // A missing header becomes a validation error, not a 500.
                idempotencyKey ?? string.Empty,
                request.Type,
                request.Amount,
                request.OccurredAt,
                request.Description);

            Result<CreatedTransaction> result = await handler.Handle(command, cancellationToken);

            if (result.IsFailure)
            {
                return ApiResults.Problem(result);
            }

            if (result.Value.Replayed)
            {
                httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            }

            TransactionResponse body = result.Value.Transaction;
            return Results.Created($"/transactions/{body.Id}", body);
        })
        .WithName("CreateTransaction")
        .WithTags(Tags.Transactions)
        .WithSummary("Registra um crédito ou débito")
        .WithDescription("Exige Idempotency-Key. Grava o lançamento e o evento na Outbox na mesma transação; não depende da consolidação.")
        .Produces<TransactionResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
