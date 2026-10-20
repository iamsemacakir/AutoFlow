using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;

namespace AutoFlow.Application.Vehicles;

public sealed record TransferVehicleCommand(Guid VehicleId, Guid NewCustomerId);

/// <summary>Moves a vehicle to another existing customer, e.g. after a private sale.</summary>
public sealed class TransferVehicleHandler(
    ICustomerRepository customers,
    IVehicleRepository vehicles,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<VehicleDetails>> HandleAsync(
        TransferVehicleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var vehicle = await vehicles.GetByIdAsync(command.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return Result.Failure<VehicleDetails>(VehicleErrors.NotFound(command.VehicleId));
        }

        if (!await customers.ExistsAsync(command.NewCustomerId, cancellationToken))
        {
            return Result.Failure<VehicleDetails>(CustomerErrors.NotFound(command.NewCustomerId));
        }

        vehicle.TransferTo(command.NewCustomerId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(VehicleDetails.From(vehicle));
    }
}
