# ADR 0003: EF Core on PostgreSQL, mapped without touching the domain

- Status: Accepted
- Date: 2026-10-21

## Context

The application layer defines `ICustomerRepository`, `IVehicleRepository` and
`IUnitOfWork` (ADR 0002). They need a real implementation. The domain model is
deliberately persistence-ignorant: private constructors, get-only properties,
value objects (`Vin`, `LicensePlate`, `EmailAddress`) and no navigation from a
vehicle to its customer. Several people (owner, service advisors) will edit
the same records, so lost updates must be detected.

## Decision

- **EF Core 10 with Npgsql** in a new `AutoFlow.Infrastructure` project.
  `AutoFlowDbContext` implements `IUnitOfWork`; repositories are thin, internal
  classes over its `DbSet`s. `AddInfrastructure(connectionString)` registers
  everything as scoped.
- **Fluent configuration only** (`IEntityTypeConfiguration<T>` classes); the
  domain gets no attributes and no EF-specific constructors. EF binds the
  existing private constructors and writes get-only properties through their
  backing fields.
- **Value objects as value converters** to their normalized string. Reading
  calls the domain factory again, so a corrupted row fails loudly.
- **Explicit snake_case names** for tables, columns, keys, indexes and
  constraints (`vehicles.vin`, `ux_vehicles_vin`) instead of a naming-convention
  package — the schema is part of the contract and is readable in the code.
- **Constraints in the database as a safety net**: unique index on `vin`,
  foreign key `vehicles.customer_id → customers.id` with `RESTRICT` (a customer
  with vehicles cannot be deleted), check constraints for non-negative mileage
  and the earliest model year.
- **Optimistic concurrency via `xmin`**: a `uint` shadow property marked as row
  version maps to PostgreSQL's system column, so no schema change and no
  domain property are needed. A stale write throws
  `DbUpdateConcurrencyException`.
- **Integration tests against real PostgreSQL** (Testcontainers,
  `postgres:17-alpine`) instead of the EF in-memory provider, which ignores
  constraints, transactions and SQL translation.

## Consequences

- The domain project still has zero dependencies; mapping lives entirely in
  infrastructure.
- Timestamps are stored as `timestamptz`; Npgsql only accepts UTC
  `DateTimeOffset`s, which is what `TimeProvider.GetUtcNow()` produces.
- Two races are only caught by the database for now: a duplicate VIN
  (unique violation) and a concurrent edit (concurrency exception). Both
  surface as 500s until the API maps them to 409 (Milestone 3).
- Running the infrastructure tests requires Docker (available on GitHub
  Actions `ubuntu-latest`).
- The schema is currently created with `EnsureCreated` in tests; the initial
  migration and `docker-compose.yml` follow as the next roadmap step.
