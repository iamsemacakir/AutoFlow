using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Persistence contract for vehicles. Entities returned by
/// <see cref="GetByIdAsync"/> are tracked: changes made to them are stored on
/// the next <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IVehicleRepository
{
    Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsWithVinAsync(Vin vin, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Vehicle>> ListByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    void Add(Vehicle vehicle);
}
