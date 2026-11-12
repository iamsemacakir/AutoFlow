using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Appointments;

namespace AutoFlow.Application.Appointments;

/// <param name="AppointmentId">The scheduled appointment to move.</param>
/// <param name="BayId">The bay to move to; the current one to change only the time.</param>
/// <param name="Start">First moment of the new slot; any offset, stored as UTC.</param>
/// <param name="End">First moment after the new slot (exclusive).</param>
public sealed record RescheduleAppointmentCommand(
    Guid AppointmentId,
    Guid BayId,
    DateTimeOffset Start,
    DateTimeOffset End);

/// <summary>
/// Moves a scheduled appointment to another slot, another bay, or both. The
/// target bay must be in service — also when only the time changes, so an
/// appointment in a bay that went out of service can only move to another
/// bay — and the new slot must not clash with other appointments; the
/// appointment's own current slot never counts as a clash. Moving to the
/// same bay and slot, a settled appointment and a slot in the past break a
/// rule (422). Nothing is saved when the move is refused.
/// </summary>
public sealed class RescheduleAppointmentHandler(
    IAppointmentRepository appointments,
    IBayRepository bays,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<AppointmentDetails>> HandleAsync(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var appointment = await appointments.GetByIdAsync(command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDetails>(AppointmentErrors.NotFound(command.AppointmentId));
        }

        var bay = await bays.GetByIdAsync(command.BayId, cancellationToken);
        if (bay is null)
        {
            return Result.Failure<AppointmentDetails>(BayErrors.NotFound(command.BayId));
        }

        appointment.Reschedule(
            bay.Id,
            AppointmentSlot.Create(command.Start, command.End),
            timeProvider.GetUtcNow());

        if (await BookingRules.CheckAsync(appointments, appointment, bay, cancellationToken) is { } error)
        {
            return Result.Failure<AppointmentDetails>(error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(AppointmentDetails.From(appointment));
    }
}
