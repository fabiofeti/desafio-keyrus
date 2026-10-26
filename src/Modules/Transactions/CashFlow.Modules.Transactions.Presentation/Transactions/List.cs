using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Common.Presentation.Endpoints;
using CashFlow.Common.Presentation.Results;
using CashFlow.Modules.Transactions.Application.Abstractions.Pagination;
using CashFlow.Modules.Transactions.Application.Transactions.List;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CashFlow.Modules.Transactions.Presentation.Transactions;

internal sealed class List : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("transactions", async (
            [FromQuery] DateOnly date,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IQueryHandler<ListTransactionsQuery, PagedResponse<TransactionListItem>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new ListTransactionsQuery(
                date, page ?? 1, pageSize ?? ListTransactionsQuery.DefaultPageSize);

            Result<PagedResponse<TransactionListItem>> result = await handler.Handle(query, cancellationToken);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiResults.Problem(result);
        })
        .WithName("ListTransactions")
        .WithTags(Tags.Transactions)
        .WithSummary("Lista os lançamentos de uma data de negócio")
        .WithDescription("date no formato YYYY-MM-DD; page a partir de 1; pageSize entre 1 e 200 (padrão 50). Ordenado por occurredAt e id.")
        .Produces<PagedResponse<TransactionListItem>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
