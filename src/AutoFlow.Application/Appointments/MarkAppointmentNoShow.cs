using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Appointments;

public sealed record MarkAppointmentNoShowCommand(Guid AppointmentId);

/// <summary>
/// Records that the vehicle did not arrive, which frees the bay for the rest
/// of the slot. Only possible once the slot has begun and while the
/// appointment is scheduled; otherwise a rule is broken (422).
/// </summary>
public sealed class MarkAppointmentNoShowHandler(
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<AppointmentDetails>> HandleAsync(
        MarkAppointmentNoShowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var appointment = await appointments.GetByIdAsync(command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDetails>(AppointmentErrors.NotFound(command.AppointmentId));
        }

        appointment.MarkNoShow(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(AppointmentDetails.From(appointment));
    }
}
