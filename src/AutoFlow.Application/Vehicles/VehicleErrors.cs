using AutoFlow.Application.Common;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Vehicles;

public static class VehicleErrors
{
    public static Error NotFound(Guid vehicleId) =>
        Error.NotFound("vehicle.not_found", $"Vehicle '{vehicleId}' was not found.");

    public static Error DuplicateVin(Vin vin)
    {
        ArgumentNullException.ThrowIfNull(vin);
        return Error.Conflict("vehicle.duplicate_vin", $"A vehicle with VIN '{vin.Value}' is already registered.");
    }
}
