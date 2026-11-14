using AutoFlow.Application.Common;
using AutoFlow.Domain.Appointments;

namespace AutoFlow.Application.Appointments;

public static class AppointmentErrors
{
    public static Error NotFound(Guid appointmentId) =>
        Error.NotFound("appointment.not_found", $"Appointment '{appointmentId}' was not found.");

    /// <summary>The target bay takes no bookings while it is out of service.</summary>
    public static Error BayOutOfService(string bayName) =>
        Error.Conflict(
            "appointment.bay_out_of_service",
            $"Bay '{bayName}' is out of service and takes no bookings.");

    /// <summary>Another vehicle already holds the bay during part of the slot.</summary>
    public static Error BayAlreadyBooked(string bayName, Appointment clash)
    {
        ArgumentNullException.ThrowIfNull(clash);
        return Error.Conflict(
            "appointment.bay_already_booked",
            $"Bay '{bayName}' is already booked {clash.Slot} (appointment '{clash.Id}').");
    }

    /// <summary>The vehicle is already booked into a bay during part of the slot.</summary>
    public static Error VehicleAlreadyBooked(Appointment clash)
    {
        ArgumentNullException.ThrowIfNull(clash);
        return Error.Conflict(
            "appointment.vehicle_already_booked",
            $"The vehicle is already booked {clash.Slot} (appointment '{clash.Id}').");
    }

    /// <summary>The schedule was asked for in a time zone this system does not know.</summary>
    public static Error UnknownTimeZone(string timeZone) =>
        Error.Validation(
            "schedule.unknown_time_zone",
            $"'{timeZone}' is not a known time zone; use an IANA id such as 'Europe/Berlin'.");
}
