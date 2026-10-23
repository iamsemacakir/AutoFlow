# ADR 0005: Lost write races surface as application exceptions and HTTP 409

- Status: Accepted
- Date: 2026-10-23

## Context

Use cases check preconditions before writing (`ExistsWithVinAsync`) and every
table carries an `xmin` concurrency token. Both leave a window: two requests
can pass the VIN check at the same moment, and two service advisors can edit
the same customer at once. The database catches both (unique index
`ux_vehicles_vin`, optimistic concurrency), but EF Core reports them as
`DbUpdateException` wrapping a `PostgresException` (SQLSTATE 23505) and
`DbUpdateConcurrencyException`. Unhandled, they become a 500, although the
request was valid and the client only needs to retry or reload.

The API must not reference EF Core or Npgsql types to tell these cases apart,
and handlers should not wrap every save in provider-specific `try/catch`.

## Decision

- **The unit of work translates.** `AutoFlowDbContext`'s explicit
  `IUnitOfWork.SaveChangesAsync` catches exactly two cases and rethrows them
  as exceptions owned by the application layer:
  `ConcurrencyConflictException` and `UniqueConstraintViolationException`
  (with the constraint name for diagnostics). The original exception stays as
  `InnerException`. Code that calls `DbContext.SaveChangesAsync` directly
  (tests, tooling) keeps EF Core's behaviour.
- **Only lost races are translated.** Foreign-key, check-constraint or
  not-null violations mean a use case let invalid data through; they remain
  bugs and stay 500.
- **They are exceptions, not `Result` errors.** They are rare, arise inside
  the commit after all business rules passed, and nothing in the use case can
  recover from them. Keeping them out of every handler signature keeps
  `Result` for expected outcomes (ADR 0002).
- **`ConflictExceptionHandler` maps both to 409** RFC 9457 problems with a
  stable `code` extension (`conflict.duplicate`,
  `conflict.concurrent_update`). Constraint names are logged at warning level
  but never returned, so clients cannot depend on the schema.
- The pre-check stays: it gives the common case a specific error
  (`vehicle.duplicate_vin`); the generic 409 only covers the race.

## Consequences

- Clients see 409 for every "someone else was faster" situation and can treat
  it uniformly (reload, retry).
- Update endpoints can later pass the client's version (ETag / `If-Match`)
  into the concurrency token, and the same mapping turns a stale write into 409
  without new plumbing.
- Translation lives in one place; a new unique index needs no extra code.
