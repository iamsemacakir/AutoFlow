using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Common;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.Appointments;

namespace AutoFlow.Application.Appointments;

/// <param name="VehicleId">The vehicle to be brought in.</param>
/// <param name="BayId">The bay to reserve.</param>
/// <param name="Start">First moment of the slot; any offset, stored as UTC.</param>
/// <param name="End">First moment after the slot (exclusive).</param>
/// <param name="Purpose">What the customer wants done.</param>
public sealed record BookAppointmentCommand(
    Guid VehicleId,
    Guid BayId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Purpose);

/// <summary>
/// Books a bay for a vehicle. The appointment belongs to the vehicle's current
/// owner and stays with them if the car is sold before the visit (ADR 0014).
/// An unknown vehicle or bay is not found; a bay out of service, or a slot
/// that clashes with another appointment in the bay or for the vehicle, is a
/// conflict; a slot off the 15-minute grid or in the past breaks a rule (422).
/// </summary>
public sealed class BookAppointmentHandler(
    IVehicleRepository vehicles,
    IBayRepository bays,
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<AppointmentDetails>> HandleAsync(
        BookAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var vehicle = await vehicles.GetByIdAsync(command.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return Result.Failure<AppointmentDetails>(VehicleErrors.NotFound(command.VehicleId));
        }

        var bay = await bays.GetByIdAsync(command.BayId, cancellationToken);
        if (bay is null)
        {
            return Result.Failure<AppointmentDetails>(BayErrors.NotFound(command.BayId));
        }

        var appointment = Appointment.Book(
            bay.Id,
            vehicle.Id,
            vehicle.CustomerId,
            AppointmentSlot.Create(command.Start, command.End),
            command.Purpose,
            timeProvider.GetUtcNow());

        if (await BookingRules.CheckAsync(appointments, appointment, bay, cancellationToken) is { } error)
        {
            return Result.Failure<AppointmentDetails>(error);
        }

        appointments.Add(appointment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(AppointmentDetails.From(appointment));
    }
}
