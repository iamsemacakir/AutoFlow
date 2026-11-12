using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Appointments;

public sealed record CancelAppointmentCommand(Guid AppointmentId, string Reason);

/// <summary>
/// Calls off a scheduled appointment before the vehicle arrives; its bay and
/// slot are free to book again at once. A reason is required. A settled
/// appointment (checked in, cancelled, no-show) breaks a rule (422).
/// </summary>
public sealed class CancelAppointmentHandler(
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<AppointmentDetails>> HandleAsync(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var appointment = await appointments.GetByIdAsync(command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDetails>(AppointmentErrors.NotFound(command.AppointmentId));
        }

        appointment.Cancel(command.Reason, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(AppointmentDetails.From(appointment));
    }
}
