# AutoFlow Roadmap

Each step is meant to be one coherent, reviewable increment with tests and
updated docs. Steps are done in order unless a dependency forces otherwise.

## Milestone 0 — Foundation ✅

- [x] Solution layout (`AutoFlow.slnx`, `Directory.Build.props`, `global.json`)
- [x] Domain: `Customer`, `EmailAddress`, `Vehicle`, `Vin`, `LicensePlate`
- [x] API host with health check, ProblemDetails and domain-error → 422 mapping
- [x] Domain unit tests and API smoke tests
- [x] GitHub Actions CI (restore, build, test)
- [x] Architecture notes and ADR 0001

## Milestone 1 — Application layer ✅

- [x] `AutoFlow.Application` project: repository contracts (`ICustomerRepository`, `IVehicleRepository`), `IUnitOfWork`, `TimeProvider` usage
- [x] Use cases: register customer, update customer, register vehicle, record mileage, transfer vehicle
- [x] Result/error type for expected failures (not found, duplicate VIN)
- [x] Unit tests for use cases with in-memory fakes

## Milestone 2 — Persistence ✅

- [x] `AutoFlow.Infrastructure` with EF Core + Npgsql: `AutoFlowDbContext` as unit of work, repositories, `AddInfrastructure`
- [x] Entity configurations, value-object conversions, unique VIN index, FK/check constraints, `xmin` concurrency token
- [x] Initial migration and `docker-compose.yml` for local PostgreSQL
- [x] Integration tests against PostgreSQL (Testcontainers) in CI

## Milestone 3 — Customer & vehicle endpoints ✅

- [x] Compose application + infrastructure in the API host; map unique-violation and concurrency conflicts to 409 problem responses
- [x] `/api/customers` CRUD with paging and search
- [x] `/api/customers/{id}/vehicles`, `/api/vehicles/{id}` incl. mileage and transfer
- [x] Request validation and consistent problem responses
- [x] API integration tests: OpenAPI contract, simultaneous requests, end-to-end workflow

## Milestone 4 — Work orders ✅

- [x] `WorkOrder` aggregate: status flow (Draft → Approved → InProgress → Completed → Invoiced, plus Cancelled), labour and part lines, net totals
- [x] Persistence: work order and line tables, migration, version bump on line changes, `IWorkOrderRepository`
- [x] Use cases: open (from the vehicle's current owner), edit lines (add labour/part, remove), approve, start, complete, cancel, get, list per vehicle (paged, newest first)
- [x] Endpoints and tests: open and history under the vehicle, get, add/remove lines, approve/start/complete/cancel actions; OpenAPI contract, race and workflow tests

## Milestone 5 — Appointments

- [x] Capacity: `Bay` (one vehicle at a time, in/out of service) and `AppointmentSlot` (UTC, 15-minute grid, half-open overlap); mechanics join as users in milestone 7
- [x] `Appointment` aggregate: booking (not in the past), double-booking rules per bay and per vehicle, rescheduling, check-in with work order, cancellation, no-show
- [x] Persistence: `bays` table (unique name), migration, `IBayRepository`
- [x] Persistence: `appointments` table, migration, exclusion constraints against double booking per bay and per vehicle, `IAppointmentRepository`
- [x] Use cases: manage bays (create, rename, take out of / return to service, get, list)
- [x] Use cases: book (for the current owner), reschedule, get; bay in service and clash checks as 409 conflicts
- [x] Use cases: check in (opens the work order in the same commit), cancel, no-show
- [ ] Use cases: day schedule
- [ ] Endpoints and tests

## Milestone 6 — Invoices

- [ ] Invoice from completed work order, VAT handling, numbering, payment status
- [ ] PDF export
- [ ] Endpoints and tests

## Milestone 7 — Authentication & authorization

- [ ] Identity/JWT, roles (Owner, ServiceAdvisor, Mechanic)
- [ ] Policy-based authorization on endpoints

## Milestone 8 — Web UI

- [ ] React + TypeScript (Vite) client: customers, vehicles, work orders, calendar, invoices
- [ ] Typed API client generated from OpenAPI

## Milestone 9 — Deployment

- [ ] Dockerfiles, compose for full stack
- [ ] Deployment pipeline and environment configuration
- [ ] Observability (structured logging, health checks, metrics)

## Done criteria for "AutoFlow finished"

All milestones checked, CI green, a seeded demo environment runnable with one
command, and README screenshots of the UI.
