namespace AutoFlow.Application.Common;

/// <summary>
/// One page of a larger, ordered result set. <see cref="Page"/> is 1-based;
/// <see cref="TotalCount"/> counts all matching items, not just this page.
/// </summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    /// <summary>Projects the items, e.g. from entities to read models, keeping the paging data.</summary>
    public PagedList<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return new([.. Items.Select(map)], Page, PageSize, TotalCount);
    }
}
