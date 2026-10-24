# ADR 0006: Customer endpoints — paging, search and deletion rules

- Status: Accepted
- Date: 2026-10-24

## Context

`/api/customers` is the first resource exposed over HTTP. The front desk
needs to find a customer quickly while they are on the phone ("Schmidt",
"anna@…", the last digits of a phone number), the list can grow into the
thousands, and customers have to be removable when they were created by
mistake. Three questions had to be settled: how paging works, what a search
matches, and when a customer may be deleted.

## Decision

- **Offset paging with totals.** List endpoints take `page` (1-based) and
  `pageSize` (default 20, at most 100) and return a `PagedList<T>` with
  `items`, `page`, `pageSize`, `totalCount` and `totalPages`. Out-of-range
  values are rejected with an `Error` of the new kind `Validation`
  (`paging.invalid_page`, `paging.invalid_page_size`), which the API maps to
  400, rather than silently clamped. A page past the end is an empty page, not
  an error. Limits live in `Paging`, so later lists (vehicles, work orders)
  share them. Keyset paging is not needed at workshop scale and would make
  "jump to page 5" impossible.
- **Search = every term matches some field.** The search text is split at
  whitespace; a customer matches when each term occurs, case-insensitively,
  in the first name, last name, e-mail address or phone number. "anna
  schmidt" therefore finds Anna Schmidt but not Anna Becker. Terms are
  literal: `%`, `_` and `\` are escaped. The text is limited to 100
  characters. Results are ordered by last name, first name, id, so pages are
  stable.
- **The filter is parameterized SQL.** The e-mail column is mapped through a
  value converter, which LINQ cannot pattern-match (`ILIKE`). The repository
  therefore writes the `WHERE` clause with `FromSqlRaw`, passing every term as
  a parameter, and lets EF Core compose ordering, paging and the count on top.
  The query is read-only (`AsNoTracking`). A trigram index can be added later
  without changing the contract if the table grows large.
- **Read use cases are handlers too.** `GetCustomerQuery` and
  `ListCustomersQuery` follow the command/handler shape of ADR 0002 (named
  `…Query` because they change nothing) and return read models.
- **A customer can only be deleted while they own no vehicles.** Vehicles
  carry the service history (work orders, invoices later), so deleting them
  along with the owner would destroy records the workshop must keep.
  `DeleteCustomerHandler` returns `customer.has_vehicles` (409) instead; the
  vehicles have to be transferred first. The foreign key
  (`ON DELETE RESTRICT`) backs this up in the database.
- `PUT` replaces name and contact details as a whole; there is no `PATCH`.

## Consequences

- Clients get one paging shape and one set of paging error codes for every
  list.
- A vehicle registered for a customer at the very moment that customer is
  being deleted makes one of the two requests hit the foreign key and fail
  with 500 (ADR 0005 keeps foreign-key violations untranslated). The window is
  tiny for a single-workshop system; if it shows up in practice, the unit of
  work can translate this violation to 409 as well.
- The search SQL names columns directly, so a column rename has to update it;
  the PostgreSQL search tests fail if it is forgotten.
