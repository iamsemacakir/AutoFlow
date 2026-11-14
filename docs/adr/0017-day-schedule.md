# ADR 0017: Day schedule — whose day, which appointments

- Status: Accepted
- Date: 2026-11-14

## Context

The front desk plans by the day: which bays are busy when, and who is
expected next. Appointments are stored as UTC slots (ADR 0013), but a
workshop's "Wednesday" is a wall-clock day in its own time zone, and twice a
year that day is 23 or 25 hours long. The schedule also has to decide which
appointments it shows and how bays without bookings, or out of service,
appear.

## Decision

- **The day is a date plus an IANA time zone**:
  `GetDayScheduleQuery(DateOnly Date, string TimeZone)`. The handler turns
  them into a half-open UTC period from the day's local midnight to the next
  one, with the .NET time zone database, so daylight-saving days have their
  real length. Where clocks jump forward at midnight the day begins at the
  first wall-clock time that exists; where they fall back, at the first of
  the two midnights. The period is returned (`Start`, `End`) so a client
  draws exactly the hours the server meant. An unknown or blank time zone
  is `schedule.unknown_time_zone` (validation, 400).
- **The time zone is a query parameter, not server configuration.** The
  calendar client knows the zone it displays in, and a single-workshop
  installation has nothing else to configure yet. A workshop-wide default
  can be added with deployment configuration (milestone 9) without changing
  the use case.
- **Every bay is a row**, ordered by name like `ListBaysHandler`, including
  bays without appointments (free capacity is what the desk looks for) and
  bays out of service (their remaining appointments must be moved, so they
  stay visible; the row carries `IsInService`).
- **An appointment belongs to every day its slot overlaps**, with the same
  half-open rule as double booking: 22:00–02:00 is on both days, 23:00–00:00
  only on the first. **Cancelled appointments are left out**, because they
  no longer hold anything; scheduled, checked-in and no-show appointments
  are shown with their status, since the desk acts on all three.
- **One query for the period**: `IAppointmentRepository.ListOverlappingAsync`
  returns the day's appointments of all bays in start order, untracked; the
  handler groups them by bay. A workshop has a handful of bays and a few
  dozen appointments a day, so nothing is paged.

## Consequences

- The schedule has no rules of its own; it shows what the booking use cases
  allowed (ADR 0016).
- A week view is several day schedules, or a later query over a longer
  period using the same repository method.
- The test machines and the production host need the IANA time zone data
  (present on the Linux images CI and deployment use).
