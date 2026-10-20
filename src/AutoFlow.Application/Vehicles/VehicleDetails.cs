using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Vehicles;

/// <summary>Read model of a vehicle returned by the vehicle use cases.</summary>
public sealed record VehicleDetails(
    Guid Id,
    Guid CustomerId,
    string Vin,
    string LicensePlate,
    string Make,
    string Model,
    int ModelYear,
    int MileageKm,
    DateTimeOffset RegisteredAt)
{
    public static VehicleDetails From(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        return new(
            vehicle.Id,
            vehicle.CustomerId,
            vehicle.Vin.Value,
            vehicle.LicensePlate.Value,
            vehicle.Make,
            vehicle.Model,
            vehicle.ModelYear,
            vehicle.MileageKm,
            vehicle.RegisteredAt);
    }
}
