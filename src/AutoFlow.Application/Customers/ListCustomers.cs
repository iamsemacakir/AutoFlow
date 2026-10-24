using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Customers;

/// <param name="Search">
/// Optional free text, split into terms at whitespace; every term must match
/// (see <see cref="ICustomerRepository.SearchAsync"/>).
/// </param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items per page, 1 to <see cref="Paging.MaxPageSize"/>.</param>
public sealed record ListCustomersQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>Lists customers page by page, optionally filtered by a search text.</summary>
public sealed class ListCustomersHandler(ICustomerRepository customers)
{
    public const int SearchMaxLength = 100;

    public async Task<Result<PagedList<CustomerDetails>>> HandleAsync(
        ListCustomersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (Paging.Validate(query.Page, query.PageSize) is { } pagingError)
        {
            return Result.Failure<PagedList<CustomerDetails>>(pagingError);
        }

        var search = query.Search?.Trim() ?? string.Empty;
        if (search.Length > SearchMaxLength)
        {
            return Result.Failure<PagedList<CustomerDetails>>(CustomerErrors.SearchTooLong(SearchMaxLength));
        }

        var terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var page = await customers.SearchAsync(terms, query.Page, query.PageSize, cancellationToken);

        return Result.Success(page.Map(CustomerDetails.From));
    }
}
