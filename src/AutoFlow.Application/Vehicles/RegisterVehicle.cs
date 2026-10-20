using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Vehicles;

public sealed record RegisterVehicleCommand(
    Guid CustomerId,
    string Vin,
    string LicensePlate,
    string Make,
    string Model,
    int ModelYear,
    int MileageKm);

/// <summary>
/// Registers a vehicle for an existing customer. A VIN identifies one physical
/// car, so it may be registered only once; a car changing hands is a transfer.
/// </summary>
public sealed class RegisterVehicleHandler(
    ICustomerRepository customers,
    IVehicleRepository vehicles,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<VehicleDetails>> HandleAsync(
        RegisterVehicleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await customers.ExistsAsync(command.CustomerId, cancellationToken))
        {
            return Result.Failure<VehicleDetails>(CustomerErrors.NotFound(command.CustomerId));
        }

        // Builds the vehicle (and validates every field) before the uniqueness check,
        // so malformed input is reported as a rule violation rather than a lookup.
        var vehicle = Vehicle.Register(
            command.CustomerId,
            command.Vin,
            command.LicensePlate,
            command.Make,
            command.Model,
            command.ModelYear,
            command.MileageKm,
            timeProvider.GetUtcNow());

        if (await vehicles.ExistsWithVinAsync(vehicle.Vin, cancellationToken))
        {
            return Result.Failure<VehicleDetails>(VehicleErrors.DuplicateVin(vehicle.Vin));
        }

        vehicles.Add(vehicle);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(VehicleDetails.From(vehicle));
    }
}
