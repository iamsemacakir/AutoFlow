using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;

namespace AutoFlow.Application.WorkOrders;

/// <param name="VehicleId">The vehicle whose service history is listed.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items per page, 1 to <see cref="Paging.MaxPageSize"/>.</param>
public sealed record ListVehicleWorkOrdersQuery(
    Guid VehicleId,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// Lists a vehicle's work orders, newest first, in every status and whoever
/// commissioned them, so the list is the vehicle's full service history even
/// across changes of owner. A vehicle in the workshop for years collects many
/// work orders, so the list is paged. An unknown vehicle is reported as not
/// found rather than as an empty list.
/// </summary>
public sealed class ListVehicleWorkOrdersHandler(IVehicleRepository vehicles, IWorkOrderRepository workOrders)
{
    public async Task<Result<PagedList<WorkOrderDetails>>> HandleAsync(
        ListVehicleWorkOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (Paging.Validate(query.Page, query.PageSize) is { } pagingError)
        {
            return Result.Failure<PagedList<WorkOrderDetails>>(pagingError);
        }

        if (await vehicles.GetByIdAsync(query.VehicleId, cancellationToken) is null)
        {
            return Result.Failure<PagedList<WorkOrderDetails>>(VehicleErrors.NotFound(query.VehicleId));
        }

        var page = await workOrders.ListByVehicleAsync(query.VehicleId, query.Page, query.PageSize, cancellationToken);

        return Result.Success(page.Map(WorkOrderDetails.From));
    }
}
