using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.Appointments;

/// <param name="AppointmentId">The scheduled appointment of the vehicle that arrived.</param>
/// <param name="MileageKm">
/// Odometer reading at arrival. When given it is also recorded on the vehicle;
/// when omitted the vehicle's last recorded mileage is used.
/// </param>
/// <param name="Complaint">
/// What the customer reports at the counter; when omitted or blank, the
/// appointment's purpose becomes the work order's complaint.
/// </param>
public sealed record CheckInAppointmentCommand(Guid AppointmentId, int? MileageKm = null, string? Complaint = null);

/// <summary>The checked-in appointment and the draft work order opened for it.</summary>
public sealed record CheckedInAppointment(AppointmentDetails Appointment, WorkOrderDetails WorkOrder);

/// <summary>
/// The vehicle of a scheduled appointment has arrived: opens a draft work
/// order for it — for the vehicle's current owner, like any intake — and
/// checks the appointment in with that work order, all in one commit
/// (ADR 0014). Early or late arrival is fine. A settled appointment breaks a
/// rule (422) and nothing is opened.
/// </summary>
public sealed class CheckInAppointmentHandler(
    IAppointmentRepository appointments,
    IVehicleRepository vehicles,
    IWorkOrderRepository workOrders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<CheckedInAppointment>> HandleAsync(
        CheckInAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var appointment = await appointments.GetByIdAsync(command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<CheckedInAppointment>(AppointmentErrors.NotFound(command.AppointmentId));
        }

        var vehicle = await vehicles.GetByIdAsync(appointment.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return Result.Failure<CheckedInAppointment>(VehicleErrors.NotFound(appointment.VehicleId));
        }

        if (command.MileageKm is { } reading)
        {
            vehicle.RecordMileage(reading);
        }

        var now = timeProvider.GetUtcNow();
        var workOrder = WorkOrder.Open(
            vehicle.Id,
            vehicle.CustomerId,
            string.IsNullOrWhiteSpace(command.Complaint) ? appointment.Purpose : command.Complaint,
            vehicle.MileageKm,
            now);

        appointment.CheckIn(workOrder.Id, now);

        workOrders.Add(workOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CheckedInAppointment(
            AppointmentDetails.From(appointment),
            WorkOrderDetails.From(workOrder)));
    }
}
