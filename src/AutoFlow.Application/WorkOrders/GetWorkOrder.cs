using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.WorkOrders;

public sealed record GetWorkOrderQuery(Guid WorkOrderId);

/// <summary>Returns one work order with all of its lines and totals.</summary>
public sealed class GetWorkOrderHandler(IWorkOrderRepository workOrders)
{
    public async Task<Result<WorkOrderDetails>> HandleAsync(
        GetWorkOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var workOrder = await workOrders.GetByIdAsync(query.WorkOrderId, cancellationToken);

        return workOrder is null
            ? Result.Failure<WorkOrderDetails>(WorkOrderErrors.NotFound(query.WorkOrderId))
            : Result.Success(WorkOrderDetails.From(workOrder));
    }
}
