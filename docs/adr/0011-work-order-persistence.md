# ADR 0011: Work order persistence — owned lines, line numbers, version bump

- Status: Accepted
- Date: 2026-11-03

## Context

ADR 0010 settled the work order aggregate. Storing it raises three questions
the customer and vehicle tables did not: how to map a child collection that
must never be loaded or saved on its own, how to keep the lines in a stable
order when they come back from the database, and how to detect two concurrent
edits when an edit only touches lines — the `xmin` concurrency token lives on
the `work_orders` row, which a line insert or delete does not change.

## Decision

- **Lines are an EF Core owned collection** (`OwnsMany`) in their own table
  `work_order_lines`, with the shadow foreign key `work_order_id`
  (`ON DELETE CASCADE`). EF Core loads them with every work order query and
  inserts or deletes them as they are added to or removed from the aggregate's
  private `_lines` list (field access, no setter on `Lines`). There is no
  `DbSet` and no repository for lines.
- **Lines carry a number.** Database rows have no order, and line ids are
  random, so the domain now numbers each line (`WorkOrderLine.Number`: one
  more than the highest number in use) and `WorkOrder.Lines` is ordered by it.
  A number is what a job card or invoice prints anyway. A unique index
  `ux_work_order_lines_work_order_id_number` enforces it and doubles as the
  foreign-key index.
- **A line change bumps the work order's version.** Before saving,
  `AutoFlowDbContext` looks for added, removed or modified lines and marks
  their (otherwise unchanged) work order's `complaint` as modified. EF Core
  then issues `UPDATE work_orders … WHERE id = @id AND xmin = @version` in the
  same transaction, so the edit that loses a race fails with
  `ConcurrencyConflictException` (409 `conflict.concurrent_update`) and every
  saved line change produces a new version of the work order. Two requests
  that both *add* a line may instead collide on the line-number index first;
  that surfaces as `UniqueConstraintViolationException` (409
  `conflict.duplicate`) — either way the loser gets a 409 and nothing is lost.
  A dedicated version column was not needed: rewriting one column with its
  current value is enough for PostgreSQL to assign a new `xmin`.
- **Enums are stored by name** (`status`, `kind` as `varchar(20)`), guarded by
  `IN (…)` check constraints generated from the enum names, as ADR 0010
  required.
- **Money and quantities use exact numerics sized by the domain limits:**
  `quantity numeric(7,2)` (≤ 10,000.00), `unit_price numeric(9,2)`
  (≤ 1,000,000.00), `approved_total numeric(15,2)` (100 lines at the maximum).
  Line and work order totals are computed by the domain, not stored, except
  the agreed `approved_total`.
- **Check constraints repeat the rules that are cheap to state in SQL**
  (quantity > 0, unit price ≥ 0, number > 0, mileage ≥ 0, part numbers only on
  part lines) as a safety net for data written outside the application.
- **References are restrictive.** `work_orders.vehicle_id` and `customer_id`
  are foreign keys with `ON DELETE RESTRICT`: a customer or vehicle with a
  service history cannot disappear underneath it.

## Consequences

- `IWorkOrderRepository` only needs `GetByIdAsync` and `Add`; the use cases
  change lines through the tracked aggregate and commit once.
- Deleting a customer who has work orders would violate
  `fk_work_orders_customers_customer_id` (a 500), even once their vehicle has
  been sold; `DeleteCustomerHandler` therefore checks first and refuses with
  `customer.has_work_orders` (409), like it does for vehicles.
- Every line table row also has its own `xmin`, so removing a line that
  another request already removed is a concurrency conflict too.
- The version bump writes one extra `UPDATE` per save that changes lines;
  work orders have at most 100 lines, so this is negligible.
