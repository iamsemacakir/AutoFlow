using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Appointments;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class AppointmentRepository(AutoFlowDbContext db) : IAppointmentRepository
{
    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        db.Appointments.AnyAsync(a => a.CustomerId == customerId, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> ListClashCandidatesAsync(
        Guid bayId,
        Guid vehicleId,
        AppointmentSlot slot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);

        // Same predicate as Appointment.OccupiesBay and AppointmentSlot.Overlaps (half-open).
        return await db.Appointments
            .AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.CheckedIn)
            .Where(a => a.BayId == bayId || a.VehicleId == vehicleId)
            .Where(a => a.Slot.Start < slot.End && slot.Start < a.Slot.End)
            .OrderBy(a => a.Slot.Start)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Appointment>> ListOverlappingAsync(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken = default)
    {
        var utcFrom = periodStart.ToUniversalTime();
        var utcTo = periodEnd.ToUniversalTime();

        // Half-open overlap, like AppointmentSlot.Overlaps.
        return await db.Appointments
            .AsNoTracking()
            .Where(a => a.Status != AppointmentStatus.Cancelled)
            .Where(a => a.Slot.Start < utcTo && utcFrom < a.Slot.End)
            .OrderBy(a => a.Slot.Start)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedList<Appointment>> ListByVehicleAsync(
        Guid vehicleId,
        AppointmentStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var matching = db.Appointments
            .AsNoTracking()
            .Where(a => a.VehicleId == vehicleId);

        if (status is { } wantedStatus)
        {
            matching = matching.Where(a => a.Status == wantedStatus);
        }

        var totalCount = await matching.CountAsync(cancellationToken);
        var items = await matching
            .OrderByDescending(a => a.Slot.Start)
            .ThenBy(a => a.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<Appointment>(items, page, pageSize, totalCount);
    }

    public void Add(Appointment appointment) => db.Appointments.Add(appointment);
}
