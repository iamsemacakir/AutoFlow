using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;

namespace AutoFlow.Application.Appointments;

/// <summary>
/// The checks that span several aggregates, shared by booking and
/// rescheduling: the bay must be in service, and the appointment must not
/// clash with another one in its bay or for its vehicle (ADR 0014). The
/// database backs the clash rule with exclusion constraints for requests
/// that pass these checks at the same moment (ADR 0015).
/// </summary>
internal static class BookingRules
{
    /// <summary>
    /// Returns the first rule <paramref name="appointment"/> breaks in
    /// <paramref name="bay"/> as it stands now, or <c>null</c> if it can be
    /// stored. A bay clash is reported before a vehicle clash.
    /// </summary>
    public static async Task<Error?> CheckAsync(
        IAppointmentRepository appointments,
        Appointment appointment,
        Bay bay,
        CancellationToken cancellationToken)
    {
        if (!bay.IsInService)
        {
            return AppointmentErrors.BayOutOfService(bay.Name);
        }

        var candidates = await appointments.ListClashCandidatesAsync(
            appointment.BayId,
            appointment.VehicleId,
            appointment.Slot,
            cancellationToken);

        if (candidates.FirstOrDefault(appointment.ClashesInBayWith) is { } bayClash)
        {
            return AppointmentErrors.BayAlreadyBooked(bay.Name, bayClash);
        }

        if (candidates.FirstOrDefault(appointment.ClashesForVehicleWith) is { } vehicleClash)
        {
            return AppointmentErrors.VehicleAlreadyBooked(vehicleClash);
        }

        return null;
    }
}
