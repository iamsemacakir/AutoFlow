using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.WorkOrders;

/// <param name="VehicleId">The vehicle handed over to the workshop.</param>
/// <param name="Complaint">What the customer reported or asked for.</param>
/// <param name="MileageKm">
/// Odometer reading at intake. When given it is also recorded on the vehicle
/// (readings never go backwards); when omitted the vehicle's last recorded
/// mileage is used.
/// </param>
public sealed record OpenWorkOrderCommand(Guid VehicleId, string Complaint, int? MileageKm = null);

/// <summary>
/// Opens a draft work order for a vehicle at intake. The work order belongs to
/// the vehicle's current owner, who commissions the work and will be invoiced
/// (ADR 0010); it stays with them even if the vehicle is sold afterwards.
/// </summary>
public sealed class OpenWorkOrderHandler(
    IVehicleRepository vehicles,
    IWorkOrderRepository workOrders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<WorkOrderDetails>> HandleAsync(
        OpenWorkOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var vehicle = await vehicles.GetByIdAsync(command.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return Result.Failure<WorkOrderDetails>(VehicleErrors.NotFound(command.VehicleId));
        }

        if (command.MileageKm is { } reading)
        {
            vehicle.RecordMileage(reading);
        }

        var workOrder = WorkOrder.Open(
            vehicle.Id,
            vehicle.CustomerId,
            command.Complaint,
            vehicle.MileageKm,
            timeProvider.GetUtcNow());

        workOrders.Add(workOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(WorkOrderDetails.From(workOrder));
    }
}
