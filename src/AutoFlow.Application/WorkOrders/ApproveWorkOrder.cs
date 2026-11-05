using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.WorkOrders;

public sealed record ApproveWorkOrderCommand(Guid WorkOrderId);

/// <summary>
/// The customer agrees to the estimate of a draft work order; its total is
/// recorded as the approved total. A draft without lines cannot be approved.
/// </summary>
public sealed class ApproveWorkOrderHandler(
    IWorkOrderRepository workOrders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<WorkOrderDetails>> HandleAsync(
        ApproveWorkOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workOrder = await workOrders.GetByIdAsync(command.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDetails>(WorkOrderErrors.NotFound(command.WorkOrderId));
        }

        workOrder.Approve(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(WorkOrderDetails.From(workOrder));
    }
}
