using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.WorkOrders;

public sealed record RemoveWorkOrderLineCommand(Guid WorkOrderId, Guid LineId);

/// <summary>
/// Removes a line from a work order that is still editable. The remaining
/// lines keep their numbers. An unknown line is reported as not found, like an
/// unknown work order, rather than as a broken business rule.
/// </summary>
public sealed class RemoveWorkOrderLineHandler(IWorkOrderRepository workOrders, IUnitOfWork unitOfWork)
{
    public async Task<Result<WorkOrderDetails>> HandleAsync(
        RemoveWorkOrderLineCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workOrder = await workOrders.GetByIdAsync(command.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDetails>(WorkOrderErrors.NotFound(command.WorkOrderId));
        }

        if (!workOrder.Lines.Any(line => line.Id == command.LineId))
        {
            return Result.Failure<WorkOrderDetails>(
                WorkOrderErrors.LineNotFound(command.WorkOrderId, command.LineId));
        }

        workOrder.RemoveLine(command.LineId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(WorkOrderDetails.From(workOrder));
    }
}
