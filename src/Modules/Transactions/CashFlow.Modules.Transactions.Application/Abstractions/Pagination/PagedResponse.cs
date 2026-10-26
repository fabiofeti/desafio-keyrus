namespace CashFlow.Modules.Transactions.Application.Abstractions.Pagination;

/// <summary>One page of a listing. Lives in this module until a second one needs it.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
