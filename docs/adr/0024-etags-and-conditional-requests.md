# ADR 0024: ETags and If-Match against lost updates

- Status: Accepted
- Date: 2026-11-16

## Context

Every aggregate carries PostgreSQL's `xmin` as a concurrency token
(ADR 0005), but only between loading and saving inside one request. A
service advisor who opens a customer, edits it for a minute and saves would
still overwrite whatever a colleague saved in that minute: the request loads
the colleague's version and checks against it. The same goes for a
work-order estimate approved after a mechanic added a line the advisor never
saw. ADR 0005 left the door open: "update endpoints can later pass the
client's version (ETag / `If-Match`) into the concurrency token".

## Decision

- **The version is the `ETag`.** `IRowVersions` (application port,
  implemented over `xmin` in the infrastructure) reads an aggregate's
  current version. `RowVersionFilter<TAggregate>`, attached to each
  `{id}` endpoint with `.WithRowVersion<T>()`, sends it as a strong tag
  (`"1234"`) on every successful answer, after a change as well, so a client
  always holds the version it last saw. Response bodies do not change.
- **`If-Match` is optional and checked before the use case.** With a
  matching tag (or `*`) the request goes ahead; with a stale, weak or
  malformed tag the filter answers **412** `request.precondition_failed`
  (with `currentETag`) and the use case never runs. A missing resource is
  left to the use case, which answers 404 as before. Without `If-Match` a
  change applies to the current version, as before: existing clients keep
  working.
- **The commit enforces the checked version.** The filter hands the version
  it checked to the unit of work (`IRowVersions.Expect`), which sets it as
  the original value of the aggregate's token before saving. A change that
  another request commits between the check and the save therefore fails
  the `UPDATE … WHERE xmin = @version` and becomes the usual 409
  `conflict.concurrent_update`.
- **Use cases stay unaware.** Versions are an HTTP concern; handlers, read
  models and the domain do not change. A work order's lines bump its version
  (ADR 0011), so a work order's tag covers its lines.

## Consequences

- 412 means "you were looking at an old version, reload"; 409 still means
  "you collided with someone in the same instant". Both are documented per
  operation and pinned by `OpenApiContractTests`.
- A tagged answer costs one extra primary-key read of `xmin`.
- The tag of a resource changes only with its root row: a change that
  touched only a child row would not show (work order lines are covered
  explicitly; other aggregates change their root with every use case).
- Lists carry no tags; a client that wants to change an item loads it first.
