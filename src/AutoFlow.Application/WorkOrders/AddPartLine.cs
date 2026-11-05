using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.WorkOrders;

/// <param name="WorkOrderId">The work order to add the line to.</param>
/// <param name="PartNumber">Optional manufacturer or supplier part number.</param>
/// <param name="Description">The part, as printed on the invoice.</param>
/// <param name="Quantity">Units (or litres), at most two decimal places.</param>
/// <param name="UnitPrice">Net price per unit in whole cents.</param>
public sealed record AddPartLineCommand(
    Guid WorkOrderId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice);

/// <summary>
/// Adds a part or consumable to a work order that is still editable (draft,
/// approved or in progress) and returns the updated work order.
/// </summary>
public sealed class AddPartLineHandler(IWorkOrderRepository workOrders, IUnitOfWork unitOfWork)
{
    public async Task<Result<WorkOrderDetails>> HandleAsync(
        AddPartLineCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workOrder = await workOrders.GetByIdAsync(command.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDetails>(WorkOrderErrors.NotFound(command.WorkOrderId));
        }

        workOrder.AddPart(command.PartNumber, command.Description, command.Quantity, command.UnitPrice);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(WorkOrderDetails.From(workOrder));
    }
}
