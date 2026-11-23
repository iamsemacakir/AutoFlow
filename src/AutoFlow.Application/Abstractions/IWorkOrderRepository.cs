using AutoFlow.Application.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Persistence contract for work orders. A work order is always loaded and
/// saved together with all of its lines. Entities returned by
/// <see cref="GetByIdAsync"/> are tracked: changes made to them, including
/// added and removed lines, are stored on the next
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IWorkOrderRepository
{
    Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if the customer commissioned any work order, in any status. Work
    /// orders stay with that customer even after the vehicle is sold.
    /// </summary>
    Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of a vehicle's work orders, newest first (by creation
    /// time, then id), each with its lines. The returned entities are
    /// read-only snapshots and are not tracked.
    /// </summary>
    Task<PagedList<WorkOrder>> ListByVehicleAsync(
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the workshop's work orders across all vehicles, newest
    /// first (by creation time, then id), optionally only those in
    /// <paramref name="status"/> and/or commissioned by
    /// <paramref name="customerId"/>. Lines come with each work order. The
    /// returned entities are read-only snapshots and are not tracked.
    /// </summary>
    Task<PagedList<WorkOrder>> ListAsync(
        WorkOrderStatus? status,
        Guid? customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    void Add(WorkOrder workOrder);
}
