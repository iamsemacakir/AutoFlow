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

## Milestone 3 — Customer & vehicle endpoints

- [x] Compose application + infrastructure in the API host; map unique-violation and concurrency conflicts to 409 problem responses
- [ ] `/api/customers` CRUD with paging and search
- [ ] `/api/customers/{id}/vehicles`, `/api/vehicles/{id}` incl. mileage and transfer
- [ ] Request validation and consistent problem responses
- [ ] API integration tests

## Milestone 4 — Work orders

- [ ] `WorkOrder` aggregate: status flow (Draft → Approved → InProgress → Completed → Invoiced), labour and part lines
- [ ] Endpoints and tests

## Milestone 5 — Appointments

- [ ] Bays/mechanics capacity, booking, overlap rules, rescheduling
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
