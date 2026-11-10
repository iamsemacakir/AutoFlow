# Architecture

AutoFlow follows a layered ("clean") architecture. Dependencies point inward:
outer layers know about inner layers, never the other way round.

```
            ┌──────────────────────┐
            │   AutoFlow.Api       │  HTTP, auth, DI composition
            └─────────┬────────────┘
                      │
   ┌──────────────────▼─────────┐   ┌──────────────────────────┐
   │ AutoFlow.Application       │◄──┤ AutoFlow.Infrastructure  │
   │ use cases, repo contracts  │   │ EF Core, PostgreSQL      │
   └──────────────────┬─────────┘   └──────────────────────────┘
                      │
            ┌─────────▼────────────┐
            │   AutoFlow.Domain    │  entities, value objects, rules
            └──────────────────────┘
```

## Layers

**Domain** — plain C#, no package references. Entities expose behaviour
(`Vehicle.RecordMileage`, `Vehicle.TransferTo`) rather than public setters, and
are created through factory methods (`Customer.Create`, `Vehicle.Register`) so
an invalid object can never exist. Value objects (`Vin`, `LicensePlate`,
`EmailAddress`) normalize and validate their input once.

An aggregate root owns its child entities and is their only entry point:
`WorkOrder` adds and removes its `WorkOrderLine`s, and its status changes
through one method per transition (`Approve`, `Start`, `Complete`,
`MarkInvoiced`, `Cancel`) that checks the state it starts from. See
[ADR 0010](adr/0010-work-order-aggregate.md).

The workshop's capacity is its bays: a `Bay` holds one vehicle at a time and
can be taken out of service. Booked time is an `AppointmentSlot`, a half-open
UTC interval on a 15-minute grid whose `Overlaps` is the one definition of a
double booking. See [ADR 0013](adr/0013-bay-capacity-and-appointment-slots.md).
An `Appointment` is its own aggregate that references its bay, vehicle and
customer by id. A rule that spans several aggregates is expressed as a
comparison between two of them (`ClashesInBayWith`, `ClashesForVehicleWith`)
that the use case evaluates against the candidates it loads and the database
backs with a constraint. Only a `Scheduled` appointment can change; `CheckIn`
(with the work order opened at arrival), `Cancel` and `MarkNoShow` settle it.
See [ADR 0014](adr/0014-appointment-aggregate.md).

**Application** — orchestrates use cases, defines repository interfaces the
infrastructure implements. Expected failures (not found, duplicate VIN) are
returned as results; exceptions are reserved for broken invariants and bugs.
See [ADR 0002](adr/0002-use-case-handlers-and-result-type.md).

- One use case = one `XxxCommand` record (`XxxQuery` for read-only use
  cases) + one `XxxHandler` class with a `HandleAsync(command, ct)` method,
  grouped by feature folder (`Customers/`, `Vehicles/`, `WorkOrders/`). No mediator library.
- Handlers load aggregates through `ICustomerRepository` / `IVehicleRepository`,
  call domain behaviour, then commit once via `IUnitOfWork.SaveChangesAsync`.
  Loaded entities are tracked, so repositories have no `Update` method.
- Handlers return `Result<T>` where `T` is a read model (`CustomerDetails`,
  `VehicleDetails`, `WorkOrderDetails`), never a domain entity, so the API
  cannot mutate aggregates by accident. Read models expose enum values by name
  (`"InProgress"`), as they are stored.
- A use case may change two aggregates in one commit when the business step
  does: `OpenWorkOrderHandler` records the intake odometer reading on the
  vehicle and opens the work order (for the vehicle's current owner) together.
- A child entity is changed only through its aggregate's use cases
  (`AddLabourLineHandler`, `AddPartLineHandler`, `RemoveWorkOrderLineHandler`
  load the `WorkOrder` and return the whole `WorkOrderDetails`, since totals
  change with every line). A child id that the aggregate does not contain is
  a lookup failure (`work_order.line_not_found`, 404), not a broken rule.
- A status change is one use case per domain transition
  (`ApproveWorkOrderHandler`, `StartWorkOrderHandler`,
  `CompleteWorkOrderHandler`, `CancelWorkOrderHandler`), stamped with
  `TimeProvider.GetUtcNow()`. The handler does not pre-check the current
  status: an illegal transition is the aggregate's `DomainException` (422).
  `MarkInvoiced` gets no use case of its own; issuing an invoice will call it.
- Errors carry a stable `Code` (`customer.not_found`, `vehicle.duplicate_vin`)
  and an `ErrorKind` that the API maps to HTTP status codes
  (Validation → 400, NotFound → 404, Conflict → 409).
- Lists are paged: queries take a 1-based `Page` and a `PageSize`
  (`Paging.DefaultPageSize` 20, `Paging.MaxPageSize` 100, checked by
  `Paging.Validate`) and return a `PagedList<T>` with the total count.
  See [ADR 0006](adr/0006-customer-endpoints-paging-search-delete.md).
  A vehicle's work order history is paged too, newest first, and includes
  work orders commissioned by earlier owners.
- `AddApplication()` registers the handlers (scoped) and `TimeProvider.System`
  unless another `TimeProvider` was registered first.

**Infrastructure** — EF Core mappings, migrations, external services.
See [ADR 0003](adr/0003-ef-core-postgresql-persistence.md).

- `AutoFlowDbContext` is the `IUnitOfWork`; `CustomerRepository`,
  `VehicleRepository`, `WorkOrderRepository`, `BayRepository` and
  `AppointmentRepository` are internal and only reachable through the
  application interfaces. `AddInfrastructure(connectionString)` wires them up.
- Mapping is fluent-only (`Persistence/Configurations/`); the domain carries
  no attributes. Value objects are stored through value converters as their
  normalized string.
- Database names are snake_case and set explicitly: tables `customers`,
  `vehicles`, `work_orders`, `work_order_lines`, `bays`, `appointments`; keys `pk_*`, foreign keys `fk_*`, indexes `ix_*`, unique indexes
  `ux_*`, check constraints `ck_*`, exclusion constraints `ex_*`.
- Every table uses PostgreSQL `xmin` as an optimistic concurrency token
  (shadow property `Version`).
- A child collection of an aggregate is an owned collection (`OwnsMany`) in
  its own table, loaded and saved only with its root: `work_order_lines`
  belongs to `work_orders`. Saving a change to the children also updates the
  root row, so the root's `xmin` is the version of the whole aggregate. Enums
  are stored by name with an `IN (…)` check constraint. See
  [ADR 0011](adr/0011-work-order-persistence.md).
- Bays have a unique name (`ux_bays_name`, exact match after trimming) so
  the schedule is unambiguous; there are few of them, so `IBayRepository.ListAsync`
  returns all bays by name without paging.
- A value object with several fields is a complex property in columns of
  its owner's table: an appointment's slot is `slot_start` and `slot_end`.
  Double booking is prevented by two PostgreSQL exclusion constraints over
  `tstzrange(slot_start, slot_end, '[)')` — one per bay, one per vehicle,
  only for appointments that hold their bay — added as raw SQL at the end
  of the otherwise generated migration because EF Core cannot model them.
  The unit of work reports their violation like a unique one. See
  [ADR 0015](adr/0015-appointment-persistence-and-exclusion-constraints.md).
- Read-only queries (searches, lists) use `AsNoTracking`; owned children
  come with their root (`ListByVehicleAsync` returns work orders with lines). Customer search
  filters with parameterized SQL (`FromSqlRaw`) because the value-converted
  e-mail column cannot be pattern-matched in LINQ; EF Core composes ordering,
  paging and counting on top.
- The schema is defined by EF Core migrations in `Persistence/Migrations/`
  (generated by `dotnet ef`, never edited by hand except to append raw SQL
  for what the model cannot express (ADR 0015), excluded from analyzers).
  `AutoFlowDbContextFactory` is the design-time entry point, so the tooling
  needs no running API. A test fails if the model has changes that no
  migration captures. See [ADR 0004](adr/0004-schema-migrations.md).

**Api** — minimal-API endpoints, request/response contracts, error handling,
authentication. Business rules do not live here.

- Endpoints are grouped per resource in a static `XxxEndpoints` class with a
  `MapXxxEndpoints()` extension (`Customers/CustomerEndpoints.cs`). They build
  a command or query, call the handler and fold the `Result` into an HTTP
  result; failures go through `Error.ToProblem()`, which writes an RFC 9457
  problem with the status from the `ErrorKind` and the error code in `code`.
- Responses are the application read models (`CustomerDetails`,
  `PagedList<CustomerDetails>`, `VehicleDetails`, `WorkOrderDetails`); request bodies are API
  records (`CustomerRequest`, `VehicleRequest`). `POST` answers 201 with a
  `Location` header, `DELETE` answers 204.
- A resource that belongs to another is created and listed under its parent
  (`/api/customers/{customerId}/vehicles`) but addressed on its own afterwards
  (`/api/vehicles/{id}`), so its URL survives a change of parent. State
  changes that are not plain replacement get their own path: an idempotent
  value with `PUT` (`/mileage`), a one-off action with `POST` (`/transfer`).
  See [ADR 0007](adr/0007-vehicle-endpoints.md).
  Work orders follow the same pattern under `/api/vehicles/{vehicleId}/work-orders`
  (open, paged history) and `/api/work-orders/{id}`. A child entity has no
  URL of its own: lines are added under `/labour-lines` and `/part-lines` and
  removed at `/lines/{lineId}`, and each answers 200 with the whole aggregate.
  A status transition is a body-less `POST` action named after the domain
  method (`/approve`, `/start`, `/complete`; `/cancel` takes a `reason`). See
  [ADR 0012](adr/0012-work-order-endpoints.md).
- Request records implement `IValidatableRequest` and check their shape
  (required, length, simple bounds, using the domain's constants); each
  endpoint group calls `WithRequestValidation()`, whose filter answers 400
  `validation.failed` with every field error before the handler runs.
  Numbers and ids in requests are nullable so a missing field is not `0`.
  See [ADR 0008](adr/0008-request-validation-and-problem-responses.md).

- `Program.cs` is the composition root: `AddApplication()` plus
  `AddInfrastructure(ConnectionStrings:AutoFlow)`. A missing connection string
  stops the host at startup instead of failing on the first request.
  `appsettings.Development.json` points at the docker-compose database.

## Error handling

Two kinds of failure, two mechanisms:

| Failure | Example | Mechanism | HTTP |
| --- | --- | --- | --- |
| Unreadable request | malformed JSON, `"page=abc"`, non-JSON body | `BadHttpRequestException` → `BadRequestExceptionHandler` | 400 / 415 |
| Request shape | missing `lastName`, `mileageKm` absent or negative | `IValidatableRequest` + validation filter | 400 with `errors` |
| No such route or method | `/api/customers/not-a-guid`, `DELETE /api/vehicles/{id}` | status-code pages | 404 / 405 |
| Expected outcome of a lookup or uniqueness check | unknown customer, duplicate VIN | `Result<T>` with `Error` | 404 / 409 |
| Invalid input or broken business rule | malformed VIN, mileage going backwards | `DomainException` | 422 |
| Lost race caught by the database | same VIN registered twice at once, same bay booked twice at once, concurrent edit | `UniqueConstraintViolationException` / `ConcurrencyConflictException` from `IUnitOfWork` | 409 |
| Bug / infrastructure failure | null reference, DB down | any other exception | 500 |

A `DomainException` signals a broken business rule. `DomainExceptionHandler`
maps it to an RFC 9457 problem response with HTTP 422.

Use cases check uniqueness before writing, but two requests can pass that
check at the same moment, and an aggregate can be edited by two requests at
once (`xmin` concurrency token). `AutoFlowDbContext`, as the `IUnitOfWork`,
translates exactly these two database outcomes into the application-level
`UniqueConstraintViolationException` and `ConcurrencyConflictException`, so
no layer above it knows EF Core or PostgreSQL error codes.
`ConflictExceptionHandler` maps both to HTTP 409 with a stable `code`
(`conflict.duplicate`, `conflict.concurrent_update`); constraint names are
logged, never returned. Other database errors (e.g. a foreign-key violation a
use case should have prevented) are bugs and stay 500. See
[ADR 0005](adr/0005-persistence-conflicts-as-409.md).

Any other unhandled exception becomes a generic 500 problem response without
leaking details.

Every problem response has the same shape, whichever component wrote it:
`ProblemDetailsConventions.Apply` (registered as
`ProblemDetailsOptions.CustomizeProblemDetails`) adds `instance` (request
path), `traceId` and, when the writer set none, a `code` derived from the
status (`ProblemCodes`: `request.malformed`, `route.not_found`,
`domain.rule_violated`, `server.error`, …). Clients switch on `code`, never
on `title` or `detail`. See
[ADR 0008](adr/0008-request-validation-and-problem-responses.md).

## Conventions

- Time is passed in explicitly (`DateTimeOffset createdAt`) so domain code is
  deterministic and testable; application handlers source it from the injected
  `TimeProvider` (tests use a fixed one).
- Identifiers are `Guid`s generated by the domain.
- Mileage is stored in kilometres as `int`.
- Money is `decimal`, net of VAT, in whole cents of the workshop's single
  currency; line totals are rounded half away from zero to cents and summed.
- Timestamps are UTC `DateTimeOffset`s, stored as `timestamptz`.
- API tests that touch data host the whole application with
  `WebApplicationFactory` against their own PostgreSQL container
  (`ApiDatabaseFixture`); nothing is faked. Besides per-endpoint tests there
  are three kinds: `OpenApiContractTests` pin every operation's id, path and
  statuses in the OpenAPI document; `ConcurrentRequestTests` fire competing
  requests at once and assert invariants that hold for every interleaving
  (one winner, documented 409/422 for the rest, never 500); workflow tests
  follow a realistic sequence of calls through `Location` headers. See
  [ADR 0009](adr/0009-api-contract-and-race-tests.md).
- Infrastructure tests run against real PostgreSQL in a Testcontainers
  container (Docker required) whose schema is built by applying the
  migrations; tests isolate themselves with fresh ids and
  VINs rather than cleaning tables.
- Test names describe behaviour: `Method_condition_expected_result`.
