using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.Appointments;

namespace AutoFlow.Application.Appointments;

/// <param name="VehicleId">The vehicle whose appointments are listed.</param>
/// <param name="Status">
/// Only appointments in this status (<c>Scheduled</c>, <c>CheckedIn</c>,
/// <c>Cancelled</c>, <c>NoShow</c>); all if omitted.
/// </param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Appointments per page, 1 to <see cref="Paging.MaxPageSize"/>.</param>
public sealed record ListVehicleAppointmentsQuery(
    Guid VehicleId,
    string? Status = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// Lists a vehicle's appointments, latest start first, so the front desk can
/// answer "when is this car booked in?" without paging through the schedule
/// day by day. Every status is included unless one is asked for
/// (<c>Scheduled</c> gives the bookings still to come), and so are bookings
/// made by earlier owners. An unknown vehicle is reported as not found rather
/// than as an empty list; an unknown status is a validation error (400).
/// </summary>
public sealed class ListVehicleAppointmentsHandler(IVehicleRepository vehicles, IAppointmentRepository appointments)
{
    public async Task<Result<PagedList<AppointmentDetails>>> HandleAsync(
        ListVehicleAppointmentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (Paging.Validate(query.Page, query.PageSize) is { } pagingError)
        {
            return Result.Failure<PagedList<AppointmentDetails>>(pagingError);
        }

        AppointmentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!EnumNames.TryParse<AppointmentStatus>(query.Status, out var parsed))
            {
                return Result.Failure<PagedList<AppointmentDetails>>(AppointmentErrors.UnknownStatus(query.Status));
            }

            status = parsed;
        }

        if (await vehicles.GetByIdAsync(query.VehicleId, cancellationToken) is null)
        {
            return Result.Failure<PagedList<AppointmentDetails>>(VehicleErrors.NotFound(query.VehicleId));
        }

        var page = await appointments.ListByVehicleAsync(
            query.VehicleId,
            status,
            query.Page,
            query.PageSize,
            cancellationToken);

        return Result.Success(page.Map(AppointmentDetails.From));
    }
}
