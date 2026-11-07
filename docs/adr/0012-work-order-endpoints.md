# ADR 0012: Work order endpoints — URLs, lines and status actions

- Status: Accepted
- Date: 2026-11-07

## Context

The work order use cases (open, edit lines, approve, start, complete, cancel,
get, history per vehicle) exist in the application layer. Exposing them over
HTTP needs answers to: where a work order lives in the URL space, how a child
line is added and removed when it has no URL of its own, how status changes
are expressed, and what each call returns.

## Decision

- **Opened and listed under the vehicle, addressed on its own.**
  `POST /api/vehicles/{vehicleId}/work-orders` opens a draft (for the
  vehicle's current owner, ADR 0010) and answers 201 with `Location:
  /api/work-orders/{id}`. `GET /api/vehicles/{vehicleId}/work-orders` is the
  vehicle's service history: paged with the shared `Paging` limits, newest
  first, including work orders of former owners. The vehicle, not the
  customer, is the parent because a work order is opened when a car is handed
  over, and the history must survive a sale. This follows ADR 0007.
- **The intake reading is optional.** `mileageKm` may be left out (the
  vehicle's last reading is used); a negative value is 400, a value below the
  vehicle's current reading is a broken rule and answers 422.
- **Lines are not resources.** A line exists only inside its work order, so
  it gets no `GET` and no `Location`: `POST /api/work-orders/{id}/labour-lines`
  and `POST /api/work-orders/{id}/part-lines` add a line, and
  `DELETE /api/work-orders/{id}/lines/{lineId}` removes one. All three answer
  200 with the whole `WorkOrderDetails`, because every line change changes the
  totals the client shows. An unknown line is 404 `work_order.line_not_found`.
  Labour and parts have different bodies (hours × rate vs. part number,
  quantity × unit price), hence two paths rather than one body with a `kind`.
- **Status changes are actions.** `POST /api/work-orders/{id}/approve`,
  `/start`, `/complete` and `/cancel` (the last with a `reason`), one per
  domain transition. A `PATCH` of `status` would hide that each transition has
  its own rules and side effects (approval records the agreed total). An
  illegal transition is 422 from the aggregate; two simultaneous changes of
  the same work order are 409 `conflict.concurrent_update` via `xmin`.
- **Request shape vs. rules.** Validation (400) checks presence and text
  lengths only; numeric ranges and precision of hours, quantities and prices
  (greater than 0, whole cents, at most two decimals) are domain rules and
  answer 422, like a VIN's format.
- Responses are `WorkOrderDetails` everywhere; there is no separate list
  summary model yet.

## Consequences

- A client can render a work order screen from any response without a
  follow-up `GET`.
- The history response embeds every line of every listed work order. With
  page sizes of at most 100 and at most 100 lines per order this is bounded;
  a slimmer summary model can be introduced if the UI needs it.
- `MarkInvoiced` has no endpoint: the invoice endpoints will call it.
