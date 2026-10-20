# ADR 0002: Plain use-case handlers and a Result type for expected failures

- Status: Accepted
- Date: 2026-10-20

## Context

The application layer needs a shape for use cases (register customer, record
mileage, transfer vehicle, …) and a way to report outcomes that are normal in
day-to-day workshop use: a customer id that does not exist, a VIN that is
already registered. Common options are a mediator library (MediatR) with
pipeline behaviours, and exceptions for every failure.

## Decision

- Each use case is a command record plus a sealed handler class with
  `HandleAsync(command, CancellationToken)`. Handlers are registered in DI and
  injected directly into endpoints. No mediator library.
- Expected failures are returned as `Result<T>` holding an `Error`
  (`Code`, `Message`, `ErrorKind`). `Result<T>` exposes `IsSuccess`, `Value`,
  `Error` and `Match`; reading the wrong side throws `InvalidOperationException`.
- Invalid input and broken invariants stay `DomainException`s thrown by the
  domain (mapped to 422). Handlers do not catch them; because they commit only
  after every rule has passed, nothing is persisted when one is thrown.
- Handlers return read models (`CustomerDetails`, `VehicleDetails`), not
  entities.

## Consequences

- Call sites are explicit and navigable ("go to definition" lands on the
  handler); there is no reflection-based dispatch to debug.
- Cross-cutting concerns (logging, transactions) are not added through a
  pipeline; if they become repetitive, decorators can be introduced later
  without changing handler signatures.
- Not-found and conflict paths are visible in method signatures and covered
  by unit tests without try/catch.
- Two failure mechanisms exist side by side; the table in
  `docs/architecture.md` states which one applies.
