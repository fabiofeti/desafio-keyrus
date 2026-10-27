using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Common.Presentation.Endpoints;
using CashFlow.Common.Presentation.OpenApi;
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
    // The attributes document the contract for OpenAPI; the validator enforces it.
    internal sealed record Request(
        [property: Description("Credit ou Debit."), AllowedValues("Credit", "Debit")] string Type,
        [property: Description("Valor positivo com até 2 casas decimais."), Range(typeof(decimal), "0.01", "999999999.99")] decimal Amount,
        [property: Description("Instante do lançamento com offset; a data de negócio é o dia desse instante em America/Sao_Paulo.")] DateTimeOffset OccurredAt,
        [property: Description("Texto livre opcional."), MaxLength(200)] string? Description = null);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("transactions", async (
            [FromHeader(Name = "Idempotency-Key")]
            [Description("Obrigatório. Mesma chave e mesmo payload repetem a resposta; mesma chave e outro payload: 409.")]
            [RegularExpression("^[A-Za-z0-9_-]{1,100}$")]
            string? idempotencyKey,
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
        .ProducesResponseHeader(StatusCodes.Status201Created, "Location", "/transactions/{id} do lançamento.")
        .ProducesResponseHeader(StatusCodes.Status201Created, "Idempotency-Replayed", "\"true\" quando a resposta repete um POST anterior com a mesma chave.")
        // Problem details with "code" (and "errors" as [{code, description, type}]
        // on validation failures), not ASP.NET's field-to-messages map.
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
