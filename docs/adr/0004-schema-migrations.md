# ADR 0004: EF Core migrations own the schema, applied explicitly

- Status: Accepted
- Date: 2026-10-22

## Context

Until now the test suite created the schema with `EnsureCreated`, which builds
tables straight from the model and cannot evolve an existing database. Work
orders, appointments and invoices will change the schema repeatedly, and a
real workshop database must be upgraded without losing data. We also need a
one-command local database for development.

## Decision

- **EF Core migrations are the single source of truth for the schema.** They
  live in `AutoFlow.Infrastructure/Persistence/Migrations/`, are generated with
  `dotnet ef migrations add` and are never edited by hand (an `.editorconfig`
  section marks them as generated code). `dotnet-ef` is pinned in a local tool
  manifest so every machine and CI use the same version as the runtime.
- **Design-time factory instead of the API as startup project.**
  `AutoFlowDbContextFactory` builds the context from
  `AUTOFLOW_CONNECTION_STRING`, falling back to the docker-compose database.
  The tooling therefore does not depend on how the API composes services.
- **Tests apply the migrations.** The Testcontainers fixture calls
  `Database.MigrateAsync()`, so every CI run proves the migrations build a
  working schema (constraints, indexes, `xmin`). A model-only test asserts
  `HasPendingModelChanges()` is false, so a mapping change without a
  migration fails the build instead of surfacing in production.
- **No automatic migration at API startup.** Migrating from inside the app
  races when several instances start, and needs DDL rights at runtime.
  Locally, run `dotnet ef database update`; in deployment (Milestone 9) apply
  an idempotent script or migration bundle as a separate step before rollout.
- **`docker-compose.yml` provides local PostgreSQL 17** (same major version as
  the tests) with a named volume and a health check. Its credentials are for
  development only.

## Consequences

- Schema changes are reviewable as code in the same commit as the mapping.
- Contributors need the .NET SDK and Docker; `dotnet tool restore` installs
  the EF tooling.
- Down migrations are generated but not exercised by tests; rollbacks in
  production are expected to be forward fixes.
