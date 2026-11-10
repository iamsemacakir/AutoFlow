using AutoFlow.Domain.Appointments;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Persistence contract for appointments. Entities returned by
/// <see cref="GetByIdAsync"/> are tracked: changes made to them are stored on
/// the next <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The appointments a booking of <paramref name="vehicleId"/> into
    /// <paramref name="bayId"/> for <paramref name="slot"/> could clash with:
    /// those that hold their bay (scheduled or checked in), overlap the slot,
    /// and are in that bay or for that vehicle. The use case decides with
    /// <see cref="Appointment.ClashesInBayWith"/> and
    /// <see cref="Appointment.ClashesForVehicleWith"/>; the database enforces
    /// the same rule for simultaneous bookings. The returned entities are
    /// read-only snapshots and are not tracked.
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListClashCandidatesAsync(
        Guid bayId,
        Guid vehicleId,
        AppointmentSlot slot,
        CancellationToken cancellationToken = default);

    void Add(Appointment appointment);
}
