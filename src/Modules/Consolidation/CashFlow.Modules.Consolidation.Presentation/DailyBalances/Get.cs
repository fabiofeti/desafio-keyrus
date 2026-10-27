using System.ComponentModel;
using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Common.Presentation.Endpoints;
using CashFlow.Common.Presentation.Results;
using CashFlow.Modules.Consolidation.Application.DailyBalances.Get;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CashFlow.Modules.Consolidation.Presentation.DailyBalances;

internal sealed class Get : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("daily-balances/{date}", async (
            [Description("Data de negócio, YYYY-MM-DD.")] DateOnly date,
            IQueryHandler<GetDailyBalanceQuery, DailyBalanceResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DailyBalanceResponse> result = await handler.Handle(new GetDailyBalanceQuery(date), cancellationToken);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiResults.Problem(result);
        })
        .WithName("GetDailyBalance")
        .WithTags(Tags.DailyBalances)
        .WithSummary("Saldo consolidado de uma data de negócio")
        .WithDescription("date no formato YYYY-MM-DD. Consistência eventual: lastUpdatedAt diz quando o saldo mudou pela última vez; dia sem lançamento consolidado devolve zeros e lastUpdatedAt nulo.")
        .Produces<DailyBalanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
