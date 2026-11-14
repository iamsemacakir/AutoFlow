# ADR 0019: Invoice persistence and consecutive numbering

- Status: Accepted
- Date: 2026-11-15

## Context

Invoice numbers must be unique and consecutive: tax auditors ask about
gaps. A work order must be invoiced exactly once, also when two advisors
press the button at the same moment. And an invoice has two child
collections (lines, payments) that must be stored with it (ADR 0018).

## Decision

- **Three tables.** `invoices` for the root, `invoice_lines` and
  `invoice_payments` as owned collections, mapped like work order lines
  (ADR 0011): loaded and saved only with their invoice, each with the `xmin`
  concurrency token. Totals are not stored; `amount_paid` and `status` are,
  so lists can filter on them and every payment updates the root row — two
  payments recorded at once cannot both pass the "not more than the
  balance" rule, the second is a concurrency conflict (409).
- **The number is `year` + `sequence` under a unique index**
  (`ux_invoices_year_sequence`). The issuing use case reads the highest
  sequence of the year (`GetLastSequenceAsync`) and takes the next one; the
  invoice is inserted in the same commit that marks the work order invoiced.
  If two requests take the same number, the index rejects the second, which
  surfaces as the usual 409 `conflict.duplicate` and can simply be repeated.
- **Why not a database sequence?** `nextval` is never rolled back: a
  request that fails after drawing a number leaves a gap. Reading
  `max + 1` and letting the unique index arbitrate consumes a number only
  when the invoice is actually stored. The price is a possible retry under
  contention, which a workshop issuing a few invoices an hour never notices.
- **One invoice per work order** is a second unique index
  (`ux_invoices_work_order_id`). The use case already refuses a work order
  that is `Invoiced` (422); the index covers the race, together with the
  work order's own concurrency token.
- **Foreign keys restrict.** An invoice references its work order and
  customer; neither can be deleted from under it. Customers with work orders
  cannot be deleted anyway (`customer.has_work_orders`), which covers
  invoiced customers too.
- **`due_date` is a `date`**, the other timestamps `timestamptz`; enums are
  stored by name with check constraints, as everywhere.

## Consequences

- Numbers restart at 1 every calendar year (UTC) without any job running at
  New Year: the first invoice of a year finds no sequence and takes 1.
- Listing invoices loads their lines and payments too; with paging (at most
  100 per page) that is two extra small queries.
