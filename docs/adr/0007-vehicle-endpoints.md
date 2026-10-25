# ADR 0007: Vehicle endpoints — nesting, mileage and transfer

- Status: Accepted
- Date: 2026-10-25

## Context

The vehicle use cases (register, record mileage, transfer) existed since
milestone 1 but had no HTTP surface. A vehicle always belongs to exactly one
customer, yet ownership changes when a car is sold, and work orders, invoices
and appointments will reference the vehicle later. Three questions had to be
settled: where a vehicle lives in the URL space, how mileage and transfer are
expressed, and whether a customer's vehicles are paged.

## Decision

- **Created and listed under the owner, addressed on its own.**
  `POST /api/customers/{customerId}/vehicles` registers a vehicle and
  `GET /api/customers/{customerId}/vehicles` lists what a customer owns. The
  created vehicle's `Location` is `/api/vehicles/{id}`. Nesting the canonical
  URL under the owner would break every stored link (from work orders,
  invoices, bookmarks) on each transfer.
- **Mileage is `PUT /api/vehicles/{id}/mileage`.** Recording the same reading
  twice is harmless (the rule is "never lower", not "always higher"), so the
  operation is idempotent and `PUT` fits. A lower reading is a broken business
  rule and answers 422.
- **Transfer is `POST /api/vehicles/{id}/transfer`.** It is an action with a
  rule attached (the new owner must exist and differ from the current one),
  not a field replacement, so it gets its own command endpoint rather than a
  `PATCH` of `customerId`. An unknown new owner is 404 (`customer.not_found`),
  the same code the register endpoint uses; transferring to the current owner
  is 422.
- **A customer's vehicles are not paged.** A workshop customer owns a handful
  of cars; fleets are out of scope for now. The list is ordered by
  registration time and id, and an unknown customer is 404 rather than an
  empty list, so a typo in the id is not mistaken for "no vehicles".
- **Lists are read-only snapshots.** `IVehicleRepository.ListByCustomerAsync`
  now uses `AsNoTracking`, like the customer search, and the contract says
  so; use cases that change a vehicle load it by id.
- Responses are `VehicleDetails`; every write returns the updated vehicle so
  clients need no follow-up `GET`.

## Consequences

- Clients never have to rewrite a vehicle URL after a sale.
- Request bodies are not yet validated before they reach the domain (missing
  fields arrive as `null`/`0` and fail domain rules with 422, a missing
  `customerId` on transfer yields 404). Consistent 400 validation responses are
  the next roadmap step.
- If fleet customers appear, the list can adopt the shared `Paging` limits,
  but that changes the response from an array to a `PagedList`, so it would be
  a deliberate breaking change of this endpoint.
