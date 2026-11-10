using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Appointments;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class AppointmentRepository(AutoFlowDbContext db) : IAppointmentRepository
{
    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

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

    public void Add(Appointment appointment) => db.Appointments.Add(appointment);
}
