using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Appointments;

/// <summary>
/// A booking of one bay for one vehicle during an <see cref="AppointmentSlot"/>.
/// The appointment is its own aggregate: whether it collides with another one
/// is decided by comparing two appointments (<see cref="ClashesInBayWith"/>,
/// <see cref="ClashesForVehicleWith"/>), which the booking use case and the
/// database constraint both enforce (ADR 0014).
/// </summary>
/// <remarks>
/// Status flow:
/// <code>
///            ┌──CheckIn──► CheckedIn
/// Scheduled ─┼──Cancel───► Cancelled
///    ▲  │    └──MarkNoShow► NoShow
///    └──┘ Reschedule
/// </code>
/// A scheduled or checked-in appointment occupies its bay for the whole slot;
/// a cancelled one or a no-show frees it.
/// </remarks>
public sealed class Appointment : Entity
{
    public const int PurposeMaxLength = 500;
    public const int CancellationReasonMaxLength = 500;

    private Appointment(
        Guid id,
        Guid bayId,
        Guid vehicleId,
        Guid customerId,
        AppointmentSlot slot,
        string purpose,
        DateTimeOffset bookedAt)
        : base(id)
    {
        BayId = bayId;
        VehicleId = vehicleId;
        CustomerId = customerId;
        Slot = slot;
        Purpose = purpose;
        BookedAt = bookedAt;
        Status = AppointmentStatus.Scheduled;
    }

    /// <summary>
    /// Rehydrates a stored appointment. The slot is a value object in its own
    /// columns, which persistence sets right after construction.
    /// </summary>
    private Appointment(
        Guid id,
        Guid bayId,
        Guid vehicleId,
        Guid customerId,
        string purpose,
        DateTimeOffset bookedAt)
        : base(id)
    {
        BayId = bayId;
        VehicleId = vehicleId;
        CustomerId = customerId;
        Slot = null!;
        Purpose = purpose;
        BookedAt = bookedAt;
    }

    public Guid BayId { get; private set; }

    public Guid VehicleId { get; }

    /// <summary>
    /// The customer who booked, taken from the vehicle's owner at booking time.
    /// Like a work order, the appointment stays with them if the car is sold.
    /// </summary>
    public Guid CustomerId { get; }

    public AppointmentSlot Slot { get; private set; }

    /// <summary>What the customer wants done ("Annual service", "Brakes squeak").</summary>
    public string Purpose { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset BookedAt { get; }

    /// <summary>When the bay or slot was last changed; null if never rescheduled.</summary>
    public DateTimeOffset? RescheduledAt { get; private set; }

    /// <summary>The work order opened when the vehicle arrived.</summary>
    public Guid? WorkOrderId { get; private set; }

    public DateTimeOffset? CheckedInAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? NoShowAt { get; private set; }

    /// <summary>True while the appointment holds its bay for its slot.</summary>
    public bool OccupiesBay => Status is AppointmentStatus.Scheduled or AppointmentStatus.CheckedIn;

    public static Appointment Book(
        Guid bayId,
        Guid vehicleId,
        Guid customerId,
        AppointmentSlot slot,
        string purpose,
        DateTimeOffset bookedAt)
    {
        EnsureBay(bayId);

        if (vehicleId == Guid.Empty)
        {
            throw new DomainException("An appointment must be for a vehicle.");
        }

        if (customerId == Guid.Empty)
        {
            throw new DomainException("An appointment must belong to a customer.");
        }

        ArgumentNullException.ThrowIfNull(slot);
        EnsureNotInThePast(slot, bookedAt);

        return new Appointment(
            Guid.NewGuid(),
            bayId,
            vehicleId,
            customerId,
            slot,
            Guard.RequiredText(purpose, "Purpose", PurposeMaxLength),
            bookedAt);
    }

    public void UpdatePurpose(string purpose)
    {
        EnsureScheduled("changed");
        Purpose = Guard.RequiredText(purpose, "Purpose", PurposeMaxLength);
    }

    /// <summary>Moves the appointment to another slot, another bay, or both.</summary>
    public void Reschedule(Guid bayId, AppointmentSlot slot, DateTimeOffset rescheduledAt)
    {
        EnsureScheduled("rescheduled");
        EnsureBay(bayId);
        ArgumentNullException.ThrowIfNull(slot);

        if (bayId == BayId && slot == Slot)
        {
            throw new DomainException("The appointment is already booked for this bay and time.");
        }

        EnsureNotInThePast(slot, rescheduledAt);

        BayId = bayId;
        Slot = slot;
        RescheduledAt = rescheduledAt;
    }

    /// <summary>
    /// The vehicle has arrived and <paramref name="workOrderId"/> was opened
    /// for it. Early or late arrival is fine: the slot is a plan, not a gate.
    /// </summary>
    public void CheckIn(Guid workOrderId, DateTimeOffset checkedInAt)
    {
        EnsureScheduled("checked in");

        if (workOrderId == Guid.Empty)
        {
            throw new DomainException("Checking in requires the work order opened for the vehicle.");
        }

        Status = AppointmentStatus.CheckedIn;
        WorkOrderId = workOrderId;
        CheckedInAt = checkedInAt;
    }

    public void Cancel(string reason, DateTimeOffset cancelledAt)
    {
        EnsureScheduled("cancelled");

        CancellationReason = Guard.RequiredText(reason, "Cancellation reason", CancellationReasonMaxLength);
        Status = AppointmentStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    /// <summary>
    /// Records that the vehicle did not arrive. Only possible once the slot
    /// has begun, so a customer is never marked absent in advance.
    /// </summary>
    public void MarkNoShow(DateTimeOffset markedAt)
    {
        EnsureScheduled("marked as a no-show");

        if (markedAt < Slot.Start)
        {
            throw new DomainException("An appointment can be marked as a no-show only once its slot has begun.");
        }

        Status = AppointmentStatus.NoShow;
        NoShowAt = markedAt;
    }

    /// <summary>
    /// True if both appointments hold the same bay at the same time, which
    /// would put two vehicles on one lift.
    /// </summary>
    public bool ClashesInBayWith(Appointment other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.BayId == BayId && Collides(other);
    }

    /// <summary>
    /// True if both appointments are for the same vehicle at the same time,
    /// which would book one car into two bays.
    /// </summary>
    public bool ClashesForVehicleWith(Appointment other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.VehicleId == VehicleId && Collides(other);
    }

    private bool Collides(Appointment other) =>
        other.Id != Id && OccupiesBay && other.OccupiesBay && Slot.Overlaps(other.Slot);

    private static void EnsureBay(Guid bayId)
    {
        if (bayId == Guid.Empty)
        {
            throw new DomainException("An appointment must reserve a bay.");
        }
    }

    /// <summary>
    /// The current quarter hour is the earliest bookable slot, so a walk-in at
    /// 08:07 can still be booked from 08:00; anything earlier is in the past.
    /// </summary>
    private static void EnsureNotInThePast(AppointmentSlot slot, DateTimeOffset now)
    {
        if (slot.Start + AppointmentSlot.MinDuration <= now)
        {
            throw new DomainException("An appointment cannot start in the past.");
        }
    }

    private void EnsureScheduled(string action)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            throw new DomainException(
                $"Only a scheduled appointment can be {action}; this one is {Describe(Status)}.");
        }
    }

    private static string Describe(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => "scheduled",
        AppointmentStatus.CheckedIn => "checked in",
        AppointmentStatus.Cancelled => "cancelled",
        AppointmentStatus.NoShow => "a no-show",
        _ => status.ToString(),
    };
}
