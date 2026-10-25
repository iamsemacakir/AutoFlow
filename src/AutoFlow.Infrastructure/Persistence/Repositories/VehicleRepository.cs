using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class VehicleRepository(AutoFlowDbContext db) : IVehicleRepository
{
    public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<bool> ExistsWithVinAsync(Vin vin, CancellationToken cancellationToken = default) =>
        db.Vehicles.AnyAsync(v => v.Vin == vin, cancellationToken);

    public Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        db.Vehicles.AnyAsync(v => v.CustomerId == customerId, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> ListByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default) =>
        await db.Vehicles
            .AsNoTracking()
            .Where(v => v.CustomerId == customerId)
            .OrderBy(v => v.RegisteredAt)
            .ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);

    public void Add(Vehicle vehicle) => db.Vehicles.Add(vehicle);
}
