using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Common;

namespace AutoFlow.Application.Appointments;

/// <param name="Date">The calendar day to show, as it is on the workshop's wall clock.</param>
/// <param name="TimeZone">
/// IANA id of the time zone <paramref name="Date"/> is read in (<c>"Europe/Berlin"</c>);
/// <see cref="GetDayScheduleQuery.Utc"/> when the caller works in UTC.
/// </param>
public sealed record GetDayScheduleQuery(DateOnly Date, string TimeZone)
{
    public const string Utc = "UTC";
}

/// <summary>
/// The schedule of one day: every bay, in and out of service, ordered by
/// name, each with its appointments of that day in start order.
/// <see cref="Start"/> and <see cref="End"/> are the day's first moment and
/// the first moment after it in UTC; across a daylight-saving change the day
/// lasts 23 or 25 hours.
/// </summary>
public sealed record DaySchedule(
    DateOnly Date,
    string TimeZone,
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<BaySchedule> Bays);

/// <summary>One bay's row of a <see cref="DaySchedule"/>.</summary>
public sealed record BaySchedule(BayDetails Bay, IReadOnlyList<AppointmentDetails> Appointments);

/// <summary>
/// Shows what the booking use cases allowed for one day (ADR 0017). An
/// appointment belongs to every day its slot overlaps, so one that runs past
/// midnight appears on both days. Cancelled appointments are left out;
/// scheduled, checked-in and no-show ones are shown with their status.
/// Bays out of service are listed too, so appointments still booked into
/// them stay visible. An unknown time zone is a validation error (400).
/// </summary>
public sealed class GetDayScheduleHandler(IBayRepository bays, IAppointmentRepository appointments)
{
    public async Task<Result<DaySchedule>> HandleAsync(
        GetDayScheduleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TryFindTimeZone(query.TimeZone, out var timeZone))
        {
            return Result.Failure<DaySchedule>(AppointmentErrors.UnknownTimeZone(query.TimeZone));
        }

        var start = StartOfDay(query.Date, timeZone);
        var end = StartOfDay(query.Date.AddDays(1), timeZone);

        var allBays = await bays.ListAsync(cancellationToken);
        var ofDay = await appointments.ListOverlappingAsync(start, end, cancellationToken);
        var byBay = ofDay.ToLookup(a => a.BayId);

        BaySchedule[] rows =
        [
            .. allBays.Select(bay => new BaySchedule(
                BayDetails.From(bay),
                [.. byBay[bay.Id].Select(AppointmentDetails.From)])),
        ];

        return Result.Success(new DaySchedule(query.Date, timeZone.Id, start, end, rows));
    }

    private static bool TryFindTimeZone(string? id, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var found))
        {
            return false;
        }

        timeZone = found;
        return true;
    }

    /// <summary>
    /// The UTC moment at which <paramref name="date"/> begins in
    /// <paramref name="timeZone"/>. Where clocks jump forward at midnight the
    /// day begins at the first wall-clock time that exists; where they fall
    /// back, at the first of the two midnights.
    /// </summary>
    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
