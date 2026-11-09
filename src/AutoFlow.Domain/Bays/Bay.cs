using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Bays;

/// <summary>
/// A working place in the workshop (a lift, an alignment rig, a wash bay) that
/// holds one vehicle at a time. Bays are the workshop's capacity: an
/// appointment reserves one bay for a time slot (ADR 0013).
/// </summary>
/// <remarks>
/// A bay that is out of service (lift under repair, rig being calibrated)
/// keeps its appointment history but takes no new bookings. Bays are never
/// deleted, so past appointments always point at an existing bay.
/// </remarks>
public sealed class Bay : Entity
{
    public const int NameMaxLength = 60;

    private Bay(Guid id, string name, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        CreatedAt = createdAt;
        IsInService = true;
    }

    /// <summary>How the workshop calls the bay ("Lift 1", "Alignment").</summary>
    public string Name { get; private set; }

    /// <summary>False while the bay is out of service and cannot be booked.</summary>
    public bool IsInService { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static Bay Create(string name, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), Guard.RequiredText(name, "Bay name", NameMaxLength), createdAt);

    public void Rename(string name) => Name = Guard.RequiredText(name, "Bay name", NameMaxLength);

    /// <summary>
    /// Stops new bookings for the bay. Appointments already booked are kept;
    /// the service advisor reschedules them to another bay.
    /// </summary>
    public void TakeOutOfService()
    {
        if (!IsInService)
        {
            throw new DomainException($"Bay '{Name}' is already out of service.");
        }

        IsInService = false;
    }

    public void ReturnToService()
    {
        if (IsInService)
        {
            throw new DomainException($"Bay '{Name}' is already in service.");
        }

        IsInService = true;
    }
}
