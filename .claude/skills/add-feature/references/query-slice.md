# Template de query

`src/Modules/Consolidation/CashFlow.Modules.Consolidation.Application/DailyBalances/Get/`

## Query e resposta

```csharp
namespace CashFlow.Modules.Consolidation.Application.DailyBalances.Get;

public sealed record GetDailyBalanceQuery(DateOnly Date) : IQuery<DailyBalanceResponse>;

public sealed record DailyBalanceResponse(
    DateOnly Date,
    decimal Credits,
    decimal Debits,
    decimal Balance,
    DateTime? LastUpdatedAt,
    string Consistency);
```

A resposta é da fatia. Duas queries que devolvem "um saldo" podem ter formatos
diferentes, e isso é correto.

## Handler

```csharp
internal sealed class GetDailyBalanceQueryHandler(IConsolidationDbContext context)
    : IQueryHandler<GetDailyBalanceQuery, DailyBalanceResponse>
{
    private const string Eventual = "eventual";

    public async Task<Result<DailyBalanceResponse>> Handle(
        GetDailyBalanceQuery query,
        CancellationToken cancellationToken)
    {
        DailyBalanceResponse? balance = await context.DailyBalances
            .AsNoTracking()
            .Where(b => b.BusinessDate == query.Date)
            .Select(b => new DailyBalanceResponse(
                b.BusinessDate, b.Credits, b.Debits, b.Credits - b.Debits, b.UpdatedAtUtc, Eventual))
            .SingleOrDefaultAsync(cancellationToken);

        // Dia sem lançamento consolidado é saldo zero, não 404: o consumidor da API
        // não precisa distinguir "não houve movimento" de "ainda não processou",
        // e lastUpdatedAt nulo já diz que nada foi consolidado para a data.
        return balance ?? new DailyBalanceResponse(query.Date, 0m, 0m, 0m, null, Eventual);
    }
}
```

## Listagem paginada (Transactions)

```csharp
public sealed record ListTransactionsQuery(DateOnly Date, int Page, int PageSize)
    : IQuery<PagedResponse<TransactionListItem>>;
```

`Where(t => t.BusinessDate == query.Date)`, `OrderBy(OccurredAt).ThenBy(Id)`,
`Skip((Page - 1) * PageSize).Take(PageSize)`, mais `CountAsync` para `totalCount`.
`PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)`
fica na Application do módulo (não em Common, até um segundo módulo precisar).

## Regras

- **Projete no banco**: `.Select(...)` antes de materializar.
- **`AsNoTracking()`** em toda leitura.
- **Sem validator em query** — o decorator só valida commands. Data inválida falha
  na rota (`{date}` parseado como `DateOnly`; formato errado = 400 pelo binding),
  e `page`/`pageSize` fora do intervalo devolvem `Error.Problem` do handler.
- **Sem cache** no MVP. Se o teste de carga mostrar necessidade, registre o número
  e proponha; não adicione por antecipação.
- **Consolidation lê só o próprio schema.** O saldo nunca é calculado somando
  `transactions.transactions` — isso seria leitura cruzada e quebraria a
  independência que o desafio pede.
