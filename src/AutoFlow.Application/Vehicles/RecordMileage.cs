using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Vehicles;

public sealed record RecordMileageCommand(Guid VehicleId, int MileageKm);

/// <summary>Stores a new odometer reading; readings never go backwards.</summary>
public sealed class RecordMileageHandler(IVehicleRepository vehicles, IUnitOfWork unitOfWork)
{
    public async Task<Result<VehicleDetails>> HandleAsync(
        RecordMileageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var vehicle = await vehicles.GetByIdAsync(command.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return Result.Failure<VehicleDetails>(VehicleErrors.NotFound(command.VehicleId));
        }

        vehicle.RecordMileage(command.MileageKm);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(VehicleDetails.From(vehicle));
    }
}
