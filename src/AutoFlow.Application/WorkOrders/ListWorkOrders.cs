using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.WorkOrders;

/// <param name="Status">
/// Only work orders in this status (<c>Draft</c>, <c>Approved</c>, <c>InProgress</c>,
/// <c>Completed</c>, <c>Invoiced</c>, <c>Cancelled</c>); all if omitted.
/// </param>
/// <param name="CustomerId">Only work orders this customer commissioned; all if omitted.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Work orders per page, 1 to <see cref="Paging.MaxPageSize"/>.</param>
public sealed record ListWorkOrdersQuery(
    string? Status = null,
    Guid? CustomerId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// Lists the workshop's work orders across all vehicles, newest first, so
/// the front desk sees what is waiting for approval, what is on the lifts and
/// what is finished but not invoiced yet without going vehicle by vehicle.
/// Filtering by customer gives the work orders that customer commissioned,
/// including those for vehicles they have sold since. An unknown customer
/// simply has none; an unknown status is a validation error (400).
/// </summary>
public sealed class ListWorkOrdersHandler(IWorkOrderRepository workOrders)
{
    public async Task<Result<PagedList<WorkOrderDetails>>> HandleAsync(
        ListWorkOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (Paging.Validate(query.Page, query.PageSize) is { } pagingError)
        {
            return Result.Failure<PagedList<WorkOrderDetails>>(pagingError);
        }

        WorkOrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!EnumNames.TryParse<WorkOrderStatus>(query.Status, out var parsed))
            {
                return Result.Failure<PagedList<WorkOrderDetails>>(WorkOrderErrors.UnknownStatus(query.Status));
            }

            status = parsed;
        }

        var page = await workOrders.ListAsync(status, query.CustomerId, query.Page, query.PageSize, cancellationToken);

        return Result.Success(page.Map(WorkOrderDetails.From));
    }
}
