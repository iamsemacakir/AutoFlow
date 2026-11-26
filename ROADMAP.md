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

## Milestone 5 — Appointments ✅

- [x] Capacity: `Bay` (one vehicle at a time, in/out of service) and `AppointmentSlot` (UTC, 15-minute grid, half-open overlap); mechanics join as users in milestone 7
- [x] `Appointment` aggregate: booking (not in the past), double-booking rules per bay and per vehicle, rescheduling, check-in with work order, cancellation, no-show
- [x] Persistence: `bays` table (unique name), migration, `IBayRepository`
- [x] Persistence: `appointments` table, migration, exclusion constraints against double booking per bay and per vehicle, `IAppointmentRepository`
- [x] Use cases: manage bays (create, rename, take out of / return to service, get, list)
- [x] Use cases: book (for the current owner), reschedule, get; bay in service and clash checks as 409 conflicts
- [x] Use cases: check in (opens the work order in the same commit), cancel, no-show
- [x] Customers who booked appointments cannot be deleted (`customer.has_appointments`, 409 instead of a foreign-key 500)
- [x] Use cases: day schedule (a date in an IANA time zone; every bay by name with the appointments overlapping that day, cancelled ones left out)
- [x] Bay endpoints and tests: list, create, get, rename, take out of / return to service
- [x] Appointment endpoints and tests: book (under the vehicle), get, reschedule; race test for simultaneous bookings
- [x] Appointment endpoints and tests: check in (answers with the opened work order), cancel, no-show
- [x] Day schedule endpoint (`GET /api/schedule`), appointment workflow test (book → move → schedule → check in → work order)

## Milestone 6 — Invoices ✅

- [x] `Invoice` aggregate: snapshot of a completed work order, VAT on the net total, year/sequence number, payments (Issued → PartiallyPaid → Paid), due date
- [x] Persistence: invoice, line and payment tables, migration, consecutive numbering and one invoice per work order by unique indexes, `IInvoiceRepository`
- [x] Use cases: issue (next number of the year, marks the work order invoiced), record payment, get, get for a work order, list (paged, by status and customer)
- [x] PDF export: `IInvoiceDocumentRenderer` port, dependency-free PDF renderer (multi-page table, totals, payments)
- [x] Endpoints and tests: issue, get (also by work order), list, record payment, PDF download; race tests for numbering and double invoicing, invoice workflow test

## Milestone 7 — Authentication & authorization ✅

- [x] Staff accounts and JWT sign-in: roles (Owner, ServiceAdvisor, Mechanic), PBKDF2 hashes, account management for owners, initial owner and migrations on startup
- [x] Policy-based authorization on every endpoint (Staff, FrontDesk, ManageWorkshop; signed-in by default), access matrix and role tests

## Milestone 8 — Web UI ✅

- [x] React + TypeScript (Vite) client: sign-in, day schedule, customers, vehicles, work orders, invoices (PDF, payments), bays, staff accounts
- [x] Typed API client generated from OpenAPI (`web/openapi.json` pinned by an API test, types checked in CI)

## Milestone 9 — Deployment ✅

- [x] Dockerfiles (API on the ASP.NET Core runtime image as non-root, web client on nginx), Compose for the full stack with the demo workshop
- [x] Deployment pipeline (images built on every push, published to GHCR for `v*` tags) and environment configuration
- [x] Observability: JSON logs, liveness/readiness checks, OpenTelemetry metrics and traces (OTLP, Prometheus) with business metrics

## Done criteria for "AutoFlow finished" ✅

- [x] All milestones checked
- [x] CI green: .NET build and tests, web client checks, Docker images, end-to-end tests
- [x] A seeded demo environment runnable with one command (`docker compose up --build`)
- [x] README screenshots of the UI (taken by the end-to-end tests against the demo stack)

## Follow-ups — web client coverage

The API offers a few front-desk operations the first version of the client
left out; each is one screen change with its tests.

- [x] Reschedule an appointment from the day schedule (bay, day, start, length; starts from where it is)
- [x] Transfer a vehicle to another customer from the vehicle page
- [x] Delete a customer from the customer page (refused with the API's reason while they have vehicles, work orders or appointments)

## Follow-ups — finishing touches

- [x] `ETag` on every single resource and `If-Match` on every change (412 for a stale version, enforced again at commit time; ADR 0024)
- [x] The web client sends `If-Match` when editing or deleting a customer and when changing a work order, and shows the newer version on 412
- [x] License (MIT)
- [x] A password change ends every session signed in with the old password (keyed password stamp in the token, no migration; ADR 0025)
- [x] The account page signs in again with the new password after a change, so only the other sessions end

## Follow-ups — conditional changes in the web client

The first `If-Match` step covered customers and work orders; the other
front-desk changes get the same protection, one page at a time.

- [x] Vehicle page: record mileage and transfer with `If-Match`; on 412 show the current vehicle (owner, reading)
- [x] Day schedule: load the opened appointment for its version; check in, no-show, cancel and reschedule with `If-Match`
- [x] Invoice page: record a payment with `If-Match`; on 412 show the current balance instead of taking the money twice
- [x] Bays page: rename and take out of / return to service with `If-Match` (the list carries no versions, so load the bay first)
- [x] Staff page: change a role, deactivate and reactivate with `If-Match` (load each account first, as on the bays page)
- [x] Work order page: adding a labour or part line sends `If-Match` too (removing lines and the status actions already did); on 412 show the current lines and keep the typed line; page tests for lines, status flow, roles and the approved-total deviation

## Follow-ups — workshop work order list

Work orders could only be found vehicle by vehicle; the front desk needs the
whole workshop at a glance: what waits for approval, what is on the lifts,
what is finished but not invoiced yet.

- [x] Use case and query: list every work order, newest first, by status and by the customer who commissioned it
- [x] `GET /api/work-orders` with `status`, `customerId` and paging (staff; pinned in the OpenAPI contract and the web client's types)
- [x] Web client: a work orders page for every role with a status filter in the address, paging and links to each work order and vehicle

## Follow-ups — a vehicle's appointments

Appointments could only be found day by day on the schedule; at the counter
the question is "when is this car booked in?", and the vehicle page should
answer it next to the work order history.

- [x] Use case and query: a vehicle's appointments, latest start first, by status, including those booked by earlier owners
- [x] `GET /api/vehicles/{vehicleId}/appointments` with `status` and paging (staff; pinned in the OpenAPI contract and the web client's types)
- [x] Web client: the vehicle page lists the vehicle's appointments for every role (bay, purpose, status; links to the day on the schedule or to the work order of the check-in)

## Follow-ups — overdue invoices

Each invoice already says whether it is overdue, but finding the ones to
chase meant paging through every invoice; the front desk needs the dunning
list on its own.

- [x] Use case and query: list invoices by whether they are overdue (unpaid after the due date, judged by today's date), combinable with status and customer
- [ ] `GET /api/invoices?overdue=true|false` (front desk; pinned in the OpenAPI contract and the web client's types)
- [ ] Web client: an "Overdue" filter on the invoices page, kept in the address with paging
