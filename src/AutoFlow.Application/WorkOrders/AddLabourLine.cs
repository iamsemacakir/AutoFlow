using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.WorkOrders;

/// <param name="WorkOrderId">The work order to add the line to.</param>
/// <param name="Description">What the mechanic does, as printed on the invoice.</param>
/// <param name="Hours">Time charged, at most two decimal places (0.25 h).</param>
/// <param name="HourlyRate">Net rate per hour in whole cents.</param>
public sealed record AddLabourLineCommand(Guid WorkOrderId, string Description, decimal Hours, decimal HourlyRate);

/// <summary>
/// Adds a block of labour to a work order that is still editable (draft,
/// approved or in progress) and returns the updated work order.
/// </summary>
public sealed class AddLabourLineHandler(IWorkOrderRepository workOrders, IUnitOfWork unitOfWork)
{
    public async Task<Result<WorkOrderDetails>> HandleAsync(
        AddLabourLineCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workOrder = await workOrders.GetByIdAsync(command.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDetails>(WorkOrderErrors.NotFound(command.WorkOrderId));
        }

        workOrder.AddLabour(command.Description, command.Hours, command.HourlyRate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(WorkOrderDetails.From(workOrder));
    }
}
