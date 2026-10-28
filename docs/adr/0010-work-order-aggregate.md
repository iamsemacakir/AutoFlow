# ADR 0010: Work order aggregate — status flow, lines and money

- Status: Accepted
- Date: 2026-10-28

## Context

The work order is the centre of the workshop: it records what the customer
reported, what was done and what will be charged, and it is what an invoice is
issued from (milestone 6). The roadmap names the happy path
Draft → Approved → InProgress → Completed → Invoiced. Before persistence and
endpoints are built on it, the domain rules have to be settled: which
transitions are legal, when lines may change, how a declined estimate ends,
how amounts are represented and which customer a work order belongs to.

## Decision

- **One aggregate root, lines inside it.** `WorkOrder` owns a list of
  `WorkOrderLine` entities and is the only way to add or remove them
  (`AddLabour`, `AddPart`, `RemoveLine`). Lines have their own `Guid` so a
  client can remove a specific one, but no repository will ever load a line on
  its own.
- **One line type with a kind.** Labour (quantity = hours, unit price = hourly
  rate) and parts (quantity = units, unit price per unit, optional part number
  normalized to upper case) share description, quantity, unit price and total.
  A `WorkOrderLineKind` discriminator keeps them in one list and, later, one
  table; `LabourTotal` and `PartsTotal` split the sum for display and invoices.
- **Explicit transition methods, no generic `ChangeStatus`.** `Approve`,
  `Start`, `Complete`, `MarkInvoiced` and `Cancel` each check the one state
  they start from and stamp a timestamp (`ApprovedAt`, `StartedAt`, …), passed
  in like every other time in the domain. An illegal transition is a
  `DomainException` (422) and leaves the work order unchanged.
- **Cancellation is a sixth, terminal status.** A customer can decline an
  estimate (Draft) or withdraw before work begins (Approved); `Cancel` requires
  a reason. Once a mechanic has started, time has been spent and must be
  completed and charged, so `InProgress` cannot be cancelled. Deleting
  declined work orders instead would lose the history of the estimate.
- **Lines may change until completion; the agreed total is kept.** Mechanics
  routinely find more work (a worn disc behind the pads), so lines stay
  editable in Draft, Approved and InProgress. `Approve` requires at least one
  line and records `ApprovedTotal`, so any later deviation from what the
  customer agreed to stays visible. `Completed`, `Invoiced` and `Cancelled`
  are read-only: what is completed is what gets invoiced.
- **Money is `decimal`, net, in whole cents, single currency.** Unit prices
  and hourly rates are 0 – 1,000,000 with at most two decimal places; zero is
  allowed so goodwill work is still documented. Quantities are > 0 and
  ≤ 10,000 with at most two decimal places (0.25 h, 4.5 l). A line total is
  quantity × unit price rounded half away from zero to cents, and the work
  order total is the sum of the rounded line totals, so it always equals what
  a printed list of lines adds up to. VAT, currency and discounts belong to
  the invoice (milestone 6); a `Money` value object can be introduced then if
  a second currency ever appears.
- **The customer is fixed when the work order is opened.** `CustomerId` is
  stored next to `VehicleId` rather than looked up through the vehicle: when a
  car is sold mid-job, the person who commissioned the work is still the one
  who is invoiced. The use case that opens a work order will take it from the
  vehicle's current owner.
- **Bounded size.** At most 100 lines and a 2,000-character complaint keep a
  single aggregate small enough to load and save as a unit.

## Consequences

- Persistence must save the work order and its lines as one unit. Because the
  `xmin` concurrency token lives on the parent row, a change that only touches
  lines would not bump it; the infrastructure step has to make line changes
  update the work order row (e.g. a version column touched on every save) so
  two concurrent edits of the same work order still conflict with 409.
- Status values are persisted as their names, not numbers, so the enum can be
  reordered without a data migration.
- Invoices will call `MarkInvoiced` in the same unit of work that creates the
  invoice, so a work order can never be invoiced twice.
- Timestamps are not checked for order (a clock skew between two API nodes
  must not block a mechanic); reports that need durations should treat them
  as recorded, not as guaranteed monotonic.
