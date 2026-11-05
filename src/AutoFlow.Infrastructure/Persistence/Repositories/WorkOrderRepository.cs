using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class WorkOrderRepository(AutoFlowDbContext db) : IWorkOrderRepository
{
    // Lines are owned by the work order, so EF Core loads them with it.
    public Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        db.WorkOrders.AnyAsync(w => w.CustomerId == customerId, cancellationToken);

    public async Task<PagedList<WorkOrder>> ListByVehicleAsync(
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var ofVehicle = db.WorkOrders.AsNoTracking().Where(w => w.VehicleId == vehicleId);

        var totalCount = await ofVehicle.CountAsync(cancellationToken);
        var items = await ofVehicle
            .OrderByDescending(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<WorkOrder>(items, page, pageSize, totalCount);
    }

    public void Add(WorkOrder workOrder) => db.WorkOrders.Add(workOrder);
}
