# ADR 0016: Booking use cases — cross-aggregate checks and their errors

- Status: Accepted
- Date: 2026-11-12

## Context

ADR 0014 put the double-booking rule into two comparisons between
appointments and left it to the booking and rescheduling use cases to
evaluate them; ADR 0015 added the database constraints for the race. Still
open: which checks the use cases make besides the clash, in which order, how
each failure is reported, and how rescheduling avoids clashing with the
appointment's own current slot.

## Decision

- **One shared check, `BookingRules.CheckAsync`**, used by
  `BookAppointmentHandler` and `RescheduleAppointmentHandler`: the target bay
  must be in service, then no appointment from
  `ListClashCandidatesAsync` may clash in the bay, then none may clash for
  the vehicle. The first broken rule is returned, so a client sees one clear
  reason.
- **The domain decides first, the cross-aggregate checks second.** Booking
  creates the `Appointment` (slot on the grid, not in the past, purpose
  given — 422 otherwise) before loading candidates, and rescheduling calls
  `Reschedule` on the tracked appointment before checking. The check then
  compares the appointment as it would be stored; because `Collides`
  ignores an appointment's own id, a rescheduled appointment never clashes
  with its old slot, so extending 09:00–11:00 to 09:00–12:00 works. A
  refused reschedule returns before `SaveChangesAsync`, so the change to the
  tracked entity is discarded with the request.
- **Conflicts are 409 with specific codes**, because each depends on the
  current state of *another* resource and can go away:
  `appointment.bay_out_of_service`, `appointment.bay_already_booked`,
  `appointment.vehicle_already_booked`. The clash messages name the slot and
  id of the appointment in the way, so the advisor can find it. A booking
  that loses the race to the database constraint is still the generic
  `conflict.duplicate` (ADR 0015).
- **A bay out of service takes no new bookings and no moves within it.**
  Rescheduling to the same bay that has since gone out of service is refused
  too; the only way forward for such an appointment is another bay (or
  cancelling it). Existing appointments are not touched when a bay goes out
  of service (ADR 0014).
- **The customer is the vehicle's owner at booking time**, read from the
  vehicle, never from the request, like opening a work order (ADR 0010).

## Consequences

- The day schedule needs no rules of its own: it lists what these use cases
  allowed, and can flag appointments in bays that are out of service.
- Opening hours, if added, become one more check in `BookingRules` rather
  than a change to either handler.
