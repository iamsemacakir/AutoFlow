using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;

namespace AutoFlow.Application.Vehicles;

public sealed record ListCustomerVehiclesQuery(Guid CustomerId);

/// <summary>
/// Lists the vehicles a customer currently owns, oldest registration first.
/// A customer owns a handful of cars at most, so the list is not paged. An
/// unknown customer is reported as not found rather than as an empty list.
/// </summary>
public sealed class ListCustomerVehiclesHandler(ICustomerRepository customers, IVehicleRepository vehicles)
{
    public async Task<Result<IReadOnlyList<VehicleDetails>>> HandleAsync(
        ListCustomerVehiclesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await customers.ExistsAsync(query.CustomerId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<VehicleDetails>>(CustomerErrors.NotFound(query.CustomerId));
        }

        var owned = await vehicles.ListByCustomerAsync(query.CustomerId, cancellationToken);

        return Result.Success<IReadOnlyList<VehicleDetails>>([.. owned.Select(VehicleDetails.From)]);
    }
}
