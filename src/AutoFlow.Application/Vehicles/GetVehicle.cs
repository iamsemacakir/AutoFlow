using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Vehicles;

public sealed record GetVehicleQuery(Guid VehicleId);

/// <summary>Looks up a single vehicle by id.</summary>
public sealed class GetVehicleHandler(IVehicleRepository vehicles)
{
    public async Task<Result<VehicleDetails>> HandleAsync(
        GetVehicleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var vehicle = await vehicles.GetByIdAsync(query.VehicleId, cancellationToken);

        return vehicle is null
            ? Result.Failure<VehicleDetails>(VehicleErrors.NotFound(query.VehicleId))
            : Result.Success(VehicleDetails.From(vehicle));
    }
}
