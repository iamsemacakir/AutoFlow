using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Appointments;

public sealed record GetAppointmentQuery(Guid AppointmentId);

/// <summary>Returns one appointment, in any status.</summary>
public sealed class GetAppointmentHandler(IAppointmentRepository appointments)
{
    public async Task<Result<AppointmentDetails>> HandleAsync(
        GetAppointmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var appointment = await appointments.GetByIdAsync(query.AppointmentId, cancellationToken);

        return appointment is null
            ? Result.Failure<AppointmentDetails>(AppointmentErrors.NotFound(query.AppointmentId))
            : Result.Success(AppointmentDetails.From(appointment));
    }
}
