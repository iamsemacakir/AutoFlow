using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.WorkOrders;

public sealed record CompleteWorkOrderCommand(Guid WorkOrderId);

/// <summary>
/// Marks the work on a work order in progress as done; from then on its
/// lines are final and it is ready to be invoiced.
/// </summary>
public sealed class CompleteWorkOrderHandler(
    IWorkOrderRepository workOrders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<WorkOrderDetails>> HandleAsync(
        CompleteWorkOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workOrder = await workOrders.GetByIdAsync(command.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDetails>(WorkOrderErrors.NotFound(command.WorkOrderId));
        }

        workOrder.Complete(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(WorkOrderDetails.From(workOrder));
    }
}
