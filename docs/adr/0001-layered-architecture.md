# ADR 0001: Layered architecture with a dependency-free domain

- Status: Accepted
- Date: 2026-10-19

## Context

AutoFlow will grow from a small API into a system with persistence, several
business areas (work orders, appointments, invoices), authentication and a web
UI. Workshop rules — VIN format, odometer readings that never go backwards,
work-order status transitions — are the core value and must be testable
without a database or web server.

## Decision

Split the solution into Domain, Application, Infrastructure and Api projects.
The Domain project has no package or project references. Entities protect
their invariants through factory methods and behaviour methods; value objects
validate and normalize on creation.

## Consequences

- Business rules are covered by fast unit tests.
- Persistence can be swapped or introduced later without touching the domain.
- EF Core mapping needs private constructors/backing fields and value-object
  conversions; this is accepted cost.
- Slightly more ceremony than a single-project API.
