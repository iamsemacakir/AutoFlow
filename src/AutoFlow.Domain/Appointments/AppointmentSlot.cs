using System.Globalization;
using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Appointments;

/// <summary>
/// The time a bay is reserved for: a half-open interval [<see cref="Start"/>,
/// <see cref="End"/>) in UTC on a 15-minute grid. Half-open means a slot that
/// ends at 10:00 and one that starts at 10:00 do not overlap, so back-to-back
/// bookings are possible.
/// </summary>
/// <remarks>
/// The grid keeps the schedule readable and makes every overlap a whole
/// number of quarter hours; it also means two equal slots always compare
/// equal regardless of the offset they were given in (ADR 0013).
/// </remarks>
public sealed record AppointmentSlot
{
    public const int GranularityMinutes = 15;

    /// <summary>The shortest bookable slot (one grid step).</summary>
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(GranularityMinutes);

    /// <summary>The longest bookable slot: a long job blocks its bay for at most a working week.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(5);

    private static readonly long GranularityTicks = TimeSpan.FromMinutes(GranularityMinutes).Ticks;

    private AppointmentSlot(DateTimeOffset start, DateTimeOffset end)
    {
        Start = start;
        End = end;
    }

    /// <summary>First moment of the slot, in UTC.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>First moment after the slot, in UTC (exclusive).</summary>
    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;

    public static AppointmentSlot Create(DateTimeOffset start, DateTimeOffset end)
    {
        var utcStart = start.ToUniversalTime();
        var utcEnd = end.ToUniversalTime();

        if (utcEnd <= utcStart)
        {
            throw new DomainException("An appointment must end after it starts.");
        }

        if (!IsOnGrid(utcStart) || !IsOnGrid(utcEnd))
        {
            throw new DomainException(
                $"Appointments start and end on a {GranularityMinutes}-minute boundary (e.g. 08:00, 08:15).");
        }

        if (utcEnd - utcStart > MaxDuration)
        {
            throw new DomainException($"An appointment can last at most {MaxDuration.TotalDays} days.");
        }

        return new AppointmentSlot(utcStart, utcEnd);
    }

    /// <summary>Starts at <paramref name="start"/> and lasts <paramref name="duration"/>.</summary>
    public static AppointmentSlot StartingAt(DateTimeOffset start, TimeSpan duration) =>
        Create(start, start + duration);

    /// <summary>True if the two slots share at least one moment.</summary>
    public bool Overlaps(AppointmentSlot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start < other.End && other.Start < End;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Start:yyyy-MM-dd HH:mm}–{End:yyyy-MM-dd HH:mm} UTC");

    private static bool IsOnGrid(DateTimeOffset utc) => utc.UtcTicks % GranularityTicks == 0;
}
