# Template de endpoint

`src/Modules/Transactions/CashFlow.Modules.Transactions.Presentation/Transactions/Create.cs`

```csharp
namespace CashFlow.Modules.Transactions.Presentation.Transactions;

internal sealed class Create : IEndpoint
{
    // O formato do fio, separado do command: o endpoint pode aceitar algo que o
    // caso de uso não aceita, e o command não aprende HTTP.
    private sealed record Request(string Type, decimal Amount, DateTimeOffset OccurredAt, string? Description);

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
                idempotencyKey ?? string.Empty,   // ausente vira erro do validator, não 500
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
        // Público por decisão do MVP (sem autenticação corporativa no desafio).
    }
}
```

## Regras

- **`IEndpoint`, descoberto por scanning.** Nenhum `MapPost` no `Program.cs`.
  A classe é `internal sealed`; a Presentation expõe só o marcador
  `{Module}Presentation` para o host achar o assembly.
- **Resolva a interface do handler pelo DI.** Os decorators já estão em volta.
- **Falha → `ApiResults.Problem(result)`.** Nunca leia `result.Error.Code` no
  endpoint para escolher status: o mapeamento vive num lugar só.
  Sucesso: `Results.Ok`, `Results.Created` ou `Results.NoContent`.
- **Sem regra de negócio.** Mapeia o request, chama o handler, traduz o resultado.
  Um `if` sobre o domínio no endpoint pertence ao handler. O `if (Replayed)`
  acima é sobre HTTP (um header), não sobre domínio.
- **Rotas no plural**, como no doc de contratos: `transactions`, `daily-balances/{date}`.
- **Tags** num `Tags.cs` da Presentation do módulo (`Transactions`, `DailyBalances`).
- **Sem `.RequireAuthorization()`** no MVP — e sem comentário fingindo que há auth.
- A Presentation **não** referencia Domain nem Infrastructure do módulo: o tipo
  do lançamento trafega como string até o command.
