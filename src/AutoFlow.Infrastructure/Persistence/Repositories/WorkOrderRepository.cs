using AutoFlow.Application.Abstractions;
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

    public void Add(WorkOrder workOrder) => db.WorkOrders.Add(workOrder);
}
