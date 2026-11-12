using AutoFlow.Domain.Appointments;

namespace AutoFlow.Application.Appointments;

/// <summary>
/// Read model of an appointment returned by the appointment use cases. The
/// slot is flattened into <see cref="Start"/> and <see cref="End"/> (UTC,
/// end exclusive); the status is its name (<c>"CheckedIn"</c>), as stored.
/// </summary>
public sealed record AppointmentDetails(
    Guid Id,
    Guid BayId,
    Guid VehicleId,
    Guid CustomerId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Purpose,
    string Status,
    DateTimeOffset BookedAt,
    DateTimeOffset? RescheduledAt,
    Guid? WorkOrderId,
    DateTimeOffset? CheckedInAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset? NoShowAt)
{
    public static AppointmentDetails From(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new(
            appointment.Id,
            appointment.BayId,
            appointment.VehicleId,
            appointment.CustomerId,
            appointment.Slot.Start,
            appointment.Slot.End,
            appointment.Purpose,
            appointment.Status.ToString(),
            appointment.BookedAt,
            appointment.RescheduledAt,
            appointment.WorkOrderId,
            appointment.CheckedInAt,
            appointment.CancelledAt,
            appointment.CancellationReason,
            appointment.NoShowAt);
    }
}
