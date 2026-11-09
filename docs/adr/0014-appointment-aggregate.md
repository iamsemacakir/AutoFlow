# ADR 0014: Appointment aggregate — lifecycle and double-booking rules

- Status: Accepted
- Date: 2026-11-09

## Context

ADR 0013 made bays the workshop's capacity and `AppointmentSlot` the unit of
booked time. An appointment ties a vehicle to a bay for a slot. The rules to
settle are: what an appointment can become, when it may still be moved, when
it stops reserving its bay, how it connects to the work order opened when the
car arrives, and where the "no double booking" rule lives when it spans
several appointments.

## Decision

- **Each appointment is its own aggregate.** A bay with all its appointments
  as one aggregate would serialize every booking for that bay through one
  row and grow without bound. Instead, `Appointment` references its bay,
  vehicle and customer by id, and the collision rule is a comparison of two
  appointments: `ClashesInBayWith` (two vehicles on one lift) and
  `ClashesForVehicleWith` (one car in two bays). The booking and
  rescheduling use cases load the overlapping appointments and refuse with a
  409 conflict; a PostgreSQL exclusion constraint will catch the race of two
  simultaneous bookings that both passed that check (ADR 0005 maps it to 409).
- **Four statuses, one way out of `Scheduled`.** `CheckIn`, `Cancel` and
  `MarkNoShow` each end the appointment; every other change (`Reschedule`,
  `UpdatePurpose`) is allowed only while it is scheduled. Settled appointments
  are history and stay read-only.
- **Occupying the bay.** Scheduled and checked-in appointments hold their bay
  for the whole slot; cancelled ones and no-shows free it immediately, so the
  slot can be rebooked. This is the predicate the exclusion constraint will
  filter on.
- **Rescheduling changes bay, slot or both in place.** The id, booking time
  and purpose survive, and `RescheduledAt` records the last move. Moving to
  the same bay and slot is rejected as a no-op, like transferring a vehicle
  to its current owner.
- **Not in the past, but walk-ins are possible.** A slot may start no earlier
  than the current quarter hour: at 08:07 the 08:00 slot is still bookable,
  07:45 is not. The same applies when rescheduling.
- **Check-in links the work order.** Arrival is recorded by checking in with
  the id of the work order opened for the vehicle; the check-in use case will
  open both in one unit of work, like the intake reading today. Early or late
  arrival is not checked: the slot is a plan, not a gate.
- **No-show only once the slot has begun**, so nobody is marked absent in
  advance by mistake. Cancelling requires a reason (at most 500 characters),
  as for work orders.
- **The customer is fixed at booking.** As with work orders (ADR 0010), the
  booking customer is taken from the vehicle's owner when the appointment is
  made and kept if the vehicle is sold before the visit.

## Consequences

- The persistence step needs the `btree_gist` extension for an exclusion
  constraint over `(bay_id, tstzrange(start, end, '[)'))` restricted to
  occupying statuses, and a second one over the vehicle.
- A bay taken out of service keeps its scheduled appointments; the day
  schedule should flag them so the advisor reschedules.
- Reminder messages and opening hours are outside this aggregate and can be
  added around it without changing its rules.
