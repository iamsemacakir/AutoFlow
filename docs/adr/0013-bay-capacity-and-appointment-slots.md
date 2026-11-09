# ADR 0013: Bay capacity and appointment slots

- Status: Accepted
- Date: 2026-11-09

## Context

Milestone 5 adds appointments: a customer books a time for their vehicle and
the workshop must not promise more than it can do. Before the appointment
itself is modelled, two questions need an answer: what the workshop's
capacity is made of, and how a booked time is represented so that "does this
booking collide with that one?" has exactly one, testable answer.

The roadmap mentions bays and mechanics. Mechanics are people who will log in
(milestone 7 introduces the `Mechanic` role); modelling them now as a
separate staff list would have to be merged into user accounts later.

## Decision

- **Capacity is a set of bays.** A `Bay` is a working place (a lift, an
  alignment rig) that holds one vehicle at a time; the number of bays in
  service is the number of vehicles that can be booked at the same time.
  A bay has a name (required, at most 60 characters) and can be taken out of
  service and returned to it. It is never deleted, so past appointments keep
  pointing at an existing bay. Taking a bay out of service blocks new
  bookings but does not touch existing ones; rescheduling them is a decision
  for the service advisor, not an automatic cascade. Name uniqueness is a
  lookup the use case will make, as with VINs.
- **Mechanics are not a scheduling resource yet.** Assigning a mechanic to an
  appointment or work order is added once mechanics exist as users with the
  `Mechanic` role (milestone 7). Until then a bay is the only thing an
  appointment reserves.
- **A booked time is an `AppointmentSlot` value object**: a half-open interval
  [start, end) in UTC. Half-open lets one booking end at 10:00 and the next
  start at 10:00 in the same bay. `Overlaps` is the single definition of a
  collision; persistence will mirror it with a PostgreSQL range exclusion
  constraint over `[start, end)`.
- **Slots sit on a 15-minute grid, last 15 minutes to 5 days.** Start and end
  are normalized to UTC and must be whole quarter hours (no seconds). The grid
  matches how workshops quote work (0.25 h), keeps the day view readable and
  rejects accidental values such as 08:07:31 from a client clock. Five days
  lets a long job (engine or gearbox) block its bay for a working week while
  catching typos in the end date.
- **No opening hours or working days in the domain yet.** Whether 07:00 or
  Saturday is bookable is workshop configuration, not a law of the domain; it
  can be added as a policy checked by the booking use case without changing
  the slot.

## Consequences

- Clients send local times with an offset; the API answers in UTC and the UI
  converts for display.
- An appointment that overlaps another in the same bay can be detected in
  memory (`Overlaps`) and in the database (exclusion constraint), so the
  check-then-insert race of two simultaneous bookings ends in a conflict
  rather than a double booking.
- Introducing mechanics later adds a second resource an appointment can
  reserve; the slot and overlap rule stay as they are.
