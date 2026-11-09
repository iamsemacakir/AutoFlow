namespace AutoFlow.Domain.Appointments;

/// <summary>
/// Lifecycle of an appointment. A scheduled appointment ends in exactly one
/// of the other three states.
/// </summary>
public enum AppointmentStatus
{
    /// <summary>Booked; the bay is reserved for the slot.</summary>
    Scheduled = 0,

    /// <summary>The vehicle arrived and a work order was opened for it. Terminal.</summary>
    CheckedIn = 1,

    /// <summary>Called off before the vehicle arrived; the bay is free again. Terminal.</summary>
    Cancelled = 2,

    /// <summary>The vehicle did not arrive; the bay is free again. Terminal.</summary>
    NoShow = 3,
}
