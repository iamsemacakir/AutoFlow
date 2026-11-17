# AutoFlow

[![CI](https://github.com/iamsemacakir/AutoFlow/actions/workflows/ci.yml/badge.svg)](https://github.com/iamsemacakir/AutoFlow/actions/workflows/ci.yml)
[![Docker](https://github.com/iamsemacakir/AutoFlow/actions/workflows/docker.yml/badge.svg)](https://github.com/iamsemacakir/AutoFlow/actions/workflows/docker.yml)

AutoFlow is a management system for independent car workshops: the day's
schedule per bay, customers and their vehicles, work orders from estimate to
completion, and invoices with VAT, payments and PDF export — built with
**C# / .NET 10**, **ASP.NET Core** and **PostgreSQL**, with a **React +
TypeScript** web client. One command starts it with a demo workshop.

![The day's schedule: one row per bay, appointments in every status](docs/screenshots/schedule.png)

| Work order in progress | Invoices with an overdue one |
| --- | --- |
| ![Work order with labour and parts](docs/screenshots/work-order.png) | ![Invoice list](docs/screenshots/invoices.png) |
| **Invoice, paid at the counter** | **The invoice as PDF** |
| ![Invoice with payments](docs/screenshots/invoice.png) | ![Invoice PDF](docs/screenshots/invoice-pdf-1.png) |

## What it does

- **Schedule** — bays as the workshop's capacity, appointments on a
  15-minute grid, double booking prevented by the domain *and* by
  PostgreSQL exclusion constraints, a time-zone-aware day view in which
  appointments are moved between bays and times, check-in that opens the
  work order.
- **Customers and vehicles** — search, VIN and plate validation, odometer
  readings that never go backwards, ownership transfer with history.
- **Work orders** — `Draft → Approved → InProgress → Completed → Invoiced`,
  labour and part lines with cent-exact totals, the approved estimate kept
  to show deviations, optimistic concurrency on the whole aggregate.
- **Invoices** — a snapshot of the finished job with VAT on the net total,
  consecutive numbers per year without gaps, part payments, overdue
  tracking and a PDF renderer without third-party dependencies.
- **Staff and roles** — JWT sign-in, Owner / Service advisor / Mechanic
  policies on every endpoint (closed by default), accounts deactivated at
  once.
- **Operations** — Docker images, Compose stack with an invented demo
  workshop, JSON logs, liveness and readiness checks, OpenTelemetry metrics
  and traces (OTLP or Prometheus).

## How it is built

- **Clean architecture**: Domain (no dependencies) ← Application (use
  cases, ports) ← Infrastructure (EF Core, PostgreSQL, PDF) and API
  (minimal APIs). Decisions are recorded in 23 [ADRs](docs/adr) and
  [docs/architecture.md](docs/architecture.md).
- **Tests on every level** (more than 850): domain and use-case unit tests,
  persistence tests against real PostgreSQL (Testcontainers), API tests
  that host the whole application, an OpenAPI contract test, race tests
  that fire simultaneous requests, an access matrix for every endpoint,
  web client tests with Vitest and Testing Library, and Playwright
  end-to-end tests against the Compose stack.
- **One contract**: the web client's types are generated from the API's
  OpenAPI document, and CI fails if either side drifts.

The [roadmap](ROADMAP.md) lists every increment in the order it was built.

## Try it

With Docker, one command starts the database, the API and the web client
with an invented demo workshop:

```bash
docker compose up --build
```

Open <http://localhost:8080> and sign in as `owner@autoflow.demo`,
`advisor@autoflow.demo` or `mechanic@autoflow.demo` — the password is
`autoflow-demo` for all three. The API itself is on <http://localhost:5080>
(`/health/ready`, `/metrics`). These are demo credentials: set
`AUTOFLOW_SIGNING_KEY` and `AUTOFLOW_OWNER_PASSWORD` before using the stack
for anything else.

## Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), and Docker
for the infrastructure and API tests (they start throw-away PostgreSQL containers).

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/AutoFlow.Api   # http://localhost:5080
```

### Demo workshop

Set `Seed__DemoData=true` (the Docker Compose setup below does) and the API
fills an empty database on start with an invented workshop: four bays (one
out of service), eight customers with their cars, today's and tomorrow's
appointments in every status, work orders from estimate to invoiced, and
paid, part-paid and overdue invoices. The day is planned around the moment
of start in `Seed__TimeZone` (default `Europe/Berlin`). Besides the initial
owner it opens `advisor@autoflow.demo` and `mechanic@autoflow.demo` with the
password `Seed__StaffPassword` (default `autoflow-demo`). Once a customer
exists nothing is seeded, so a real installation is never touched.

### Web client

The React + TypeScript client lives in `web/` (Node.js 22). With the API
running on `localhost:5080`:

```bash
cd web
npm ci
npm run dev          # http://localhost:5173, forwards /api to the API
npm test             # Vitest + Testing Library
npm run build        # type check and production build into dist/
```

End-to-end tests (Playwright) drive the real stack: start it with
`docker compose up --build`, then run `npx playwright install chromium`
once and `npm run e2e` in `web/`. CI does the same on every push.

The client remembers the `ETag` of every resource it loads and sends it as
`If-Match` when it edits or deletes a customer, records a vehicle's mileage
or transfers it, or changes a work order; if a
colleague got there first, it says so and shows their version instead of
overwriting it.

Sign in with an account of the API (see "Signing in" below). The client's
API types are generated from `web/openapi.json`, a committed copy of the
API's OpenAPI document: after changing the API's contract, run the API
tests once with `AUTOFLOW_UPDATE_OPENAPI=1` to refresh the copy, then
`npm run generate:api`. CI fails if either step was forgotten.

### Local database

`docker compose up -d db` starts PostgreSQL 17 on `localhost:5432`
(database, user and password `autoflow`). The schema is managed by EF Core
migrations; the `dotnet ef` tool is pinned in `.config/dotnet-tools.json`.

```bash
docker compose up -d db            # start only PostgreSQL
dotnet tool restore                # installs dotnet-ef
dotnet ef database update --project src/AutoFlow.Infrastructure
```

The API reads its connection string from `ConnectionStrings:AutoFlow`
(`appsettings.Development.json` points at this database; elsewhere set the
`ConnectionStrings__AutoFlow` environment variable — the API refuses to start
without it). To target another database with `dotnet ef`, set
`AUTOFLOW_CONNECTION_STRING`. After changing a mapping, add a migration (the test suite fails
until you do):

```bash
dotnet ef migrations add <Name> --project src/AutoFlow.Infrastructure \
  --output-dir Persistence/Migrations
```

Useful endpoints while running in Development:

- `GET /health` — liveness check
- `GET /openapi/v1.json` — OpenAPI document

### Signing in

Every request carries a bearer token from `POST /api/auth/token`. On a new
installation the API creates the first owner from configuration while no
account exists yet:

```bash
export Auth__InitialOwner__Email=owner@example.com
export Auth__InitialOwner__Password='at least ten characters'
dotnet run --project src/AutoFlow.Api

TOKEN=$(curl -s localhost:5080/api/auth/token -H 'Content-Type: application/json' \
  -d '{"email":"owner@example.com","password":"at least ten characters"}' | jq -r .accessToken)
curl -s localhost:5080/api/auth/me -H "Authorization: Bearer $TOKEN"
```

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `POST /api/auth/token` | Sign in (`email`, `password`); returns `accessToken`, `expiresAt` and the account | 200, 400, 401 |
| `GET /api/auth/me` | The signed-in account | 200, 401 |
| `POST /api/auth/me/password` | Change your own password (`currentPassword`, `newPassword`) | 204, 400, 401, 422 |
| `GET /api/users` | Staff accounts (owners only) | 200, 401, 403 |
| `POST /api/users` | Open an account (`email`, `displayName`, `role`, `password`) | 201 + `Location`, 400, 401, 403, 409, 422 |
| `GET /api/users/{id}` | One account | 200, 401, 403, 404 |
| `PUT /api/users/{id}/role` | Change the role (`Owner`, `ServiceAdvisor`, `Mechanic`) | 200, 400, 401, 403, 404, 409, 412 |
| `POST /api/users/{id}/deactivate` / `reactivate` | Close or reopen an account; a closed account's tokens stop working at once | 200, 401, 403, 404, 409, 412, 422 |

Who may do what:

| Area | Mechanic | Service advisor | Owner |
| --- | --- | --- | --- |
| Read customers, vehicles, work orders, bays, appointments, the day schedule | ✅ | ✅ | ✅ |
| Record mileage; add/remove work order lines; start and complete work | ✅ | ✅ | ✅ |
| Register customers and vehicles, transfer vehicles | — | ✅ | ✅ |
| Open, approve and cancel work orders; book and settle appointments | — | ✅ | ✅ |
| Invoices and payments | — | ✅ | ✅ |
| Bays and staff accounts | — | — | ✅ |

Every API path needs a token (401 `auth.unauthenticated` without one, 403
`auth.forbidden` without the role) except sign-in, `/health` and the
OpenAPI document. The matrix is pinned by `AuthorizationMatrixTests`.

Passwords need 10 to 128 characters. Tokens are signed with
`Auth:SigningKey` (set `Auth__SigningKey` to a long random value outside
development) and last `Auth:TokenLifetime` (8 hours by default). The
workshop always keeps an active owner.

### Customers API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `GET /api/customers?search=&page=1&pageSize=20` | List customers by name; every search term must match first name, last name, e-mail or phone | 200, 400 |
| `GET /api/customers/{id}` | Get one customer | 200, 404 |
| `POST /api/customers` | Register a customer (`firstName`, `lastName`, `email`, `phoneNumber?`) | 201 + `Location`, 400, 422 |
| `PUT /api/customers/{id}` | Replace name and contact details | 200, 400, 404, 409, 412, 422 |
| `DELETE /api/customers/{id}` | Delete a customer who owns no vehicles and has no work orders or appointments | 204, 404, 409, 412 |

Every single resource (`/api/customers/{id}`, `/api/vehicles/{id}`,
`/api/work-orders/{id}`, `/api/bays/{id}`, `/api/appointments/{id}`,
`/api/invoices/{id}`, `/api/users/{id}`) answers with its version as a strong
`ETag`, also after a change. Send it back as `If-Match` with a change, and the
API refuses with 412 `request.precondition_failed` if someone changed the
resource since you loaded it, instead of overwriting their change; without
`If-Match` a change applies to whatever is current (ADR 0024).

```bash
curl -si localhost:5080/api/customers/<customer-id> | grep -i etag    # ETag: "1234"
curl -s -X PUT localhost:5080/api/customers/<customer-id> -H 'If-Match: "1234"' -H 'Content-Type: application/json' \
  -d '{"firstName":"Anna","lastName":"Meyer","email":"anna@example.com"}'
```

`POST`/`PUT` bodies are validated first: missing, blank or overlong fields
answer 400 with every field error at once; a well-formed request that breaks a
business rule (bad e-mail syntax, invalid VIN, mileage going backwards,
approving an empty work order) answers 422.

### Vehicles API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `GET /api/customers/{customerId}/vehicles` | List a customer's vehicles, oldest registration first | 200, 404 |
| `POST /api/customers/{customerId}/vehicles` | Register a vehicle (`vin`, `licensePlate`, `make`, `model`, `modelYear`, `mileageKm`) | 201 + `Location`, 400, 404, 409, 422 |
| `GET /api/vehicles/{id}` | Get one vehicle | 200, 404 |
| `PUT /api/vehicles/{id}/mileage` | Record an odometer reading (`mileageKm`); readings never go backwards | 200, 400, 404, 409, 412, 422 |
| `POST /api/vehicles/{id}/transfer` | Transfer the vehicle to another customer (`customerId`) | 200, 400, 404, 409, 412, 422 |

A VIN can be registered only once (`vehicle.duplicate_vin`, 409); a car that
changes hands is transferred, and keeps its `/api/vehicles/{id}` URL.

```bash
curl -s localhost:5080/api/customers -H 'Content-Type: application/json' \
  -d '{"firstName":"Anna","lastName":"Schmidt","email":"anna@example.com"}'
curl -s 'localhost:5080/api/customers?search=schmidt'
curl -s localhost:5080/api/customers/<customer-id>/vehicles -H 'Content-Type: application/json' \
  -d '{"vin":"WVWZZZ1JZXW000001","licensePlate":"M-AB 123","make":"Volkswagen","model":"Golf","modelYear":2019,"mileageKm":50000}'
curl -s -X PUT localhost:5080/api/vehicles/<vehicle-id>/mileage -H 'Content-Type: application/json' -d '{"mileageKm":62500}'
```

### Work orders API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `GET /api/vehicles/{vehicleId}/work-orders?page=1&pageSize=20` | A vehicle's service history, newest first, including work orders of former owners | 200, 400, 404 |
| `POST /api/vehicles/{vehicleId}/work-orders` | Open a draft for the vehicle's current owner (`complaint`, `mileageKm?`); a given reading is also recorded on the vehicle | 201 + `Location`, 400, 404, 409, 422 |
| `GET /api/work-orders/{id}` | Get one work order with its lines and totals | 200, 404 |
| `POST /api/work-orders/{id}/labour-lines` | Add labour (`description`, `hours`, `hourlyRate`) | 200, 400, 404, 409, 412, 422 |
| `POST /api/work-orders/{id}/part-lines` | Add a part (`partNumber?`, `description`, `quantity`, `unitPrice`) | 200, 400, 404, 409, 412, 422 |
| `DELETE /api/work-orders/{id}/lines/{lineId}` | Remove a line; the other lines keep their numbers | 200, 404, 409, 412, 422 |
| `POST /api/work-orders/{id}/approve` | The customer agrees to the estimate (draft with lines); the total is kept as `approvedTotal` | 200, 404, 409, 412, 422 |
| `POST /api/work-orders/{id}/start` | A mechanic starts work on an approved work order | 200, 404, 409, 412, 422 |
| `POST /api/work-orders/{id}/complete` | Work is done; the lines become final | 200, 404, 409, 412, 422 |
| `POST /api/work-orders/{id}/cancel` | Cancel a draft or approved work order (`reason`) | 200, 400, 404, 409, 412, 422 |

Amounts are net, in the workshop's currency: quantities and hours have at
most two decimals, prices are whole cents, and each line total is rounded half
away from zero to cents. Lines have no URL of their own; every line change
answers with the whole work order and its new totals. Lines can be changed
until the work order is completed (422 afterwards).

Status flow: `Draft → Approved → InProgress → Completed → Invoiced`, and
`Draft`/`Approved → Cancelled`. Each step is its own `POST` action; a step
from the wrong status is 422, and two changes to the same work order at once
leave one of them with 409 `conflict.concurrent_update`.

```bash
curl -s localhost:5080/api/vehicles/<vehicle-id>/work-orders -H 'Content-Type: application/json' \
  -d '{"complaint":"Grinding noise when braking","mileageKm":48950}'
curl -s localhost:5080/api/work-orders/<work-order-id>/labour-lines -H 'Content-Type: application/json' \
  -d '{"description":"Replace front pads and discs","hours":1.5,"hourlyRate":95}'
curl -s -X POST localhost:5080/api/work-orders/<work-order-id>/approve
```

### Bays API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `GET /api/bays` | Every bay, in and out of service, ordered by name (unpaged) | 200 |
| `POST /api/bays` | Add a bay, in service (`name`, unique) | 201 + `Location`, 400, 409, 422 |
| `GET /api/bays/{id}` | Get one bay | 200, 404 |
| `PUT /api/bays/{id}` | Rename a bay (`name`, unique) | 200, 400, 404, 409, 412, 422 |
| `POST /api/bays/{id}/take-out-of-service` | Stop new bookings (lift under repair); booked appointments stay until moved | 200, 404, 409, 412, 422 |
| `POST /api/bays/{id}/return-to-service` | Take bookings again | 200, 404, 409, 412, 422 |

A bay is the workshop's unit of capacity: one vehicle at a time. Names are
compared after trimming; a name in use by another bay is `bay.duplicate_name`
(409). Bays are never deleted.

```bash
curl -s localhost:5080/api/bays -H 'Content-Type: application/json' -d '{"name":"Lift 1"}'
curl -s -X POST localhost:5080/api/bays/<bay-id>/take-out-of-service
```

### Appointments API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `POST /api/vehicles/{vehicleId}/appointments` | Book a bay for the vehicle's current owner (`bayId`, `start`, `end`, `purpose`) | 201 + `Location`, 400, 404, 409, 422 |
| `GET /api/appointments/{id}` | Get one appointment, in any status | 200, 404 |
| `POST /api/appointments/{id}/reschedule` | Move a scheduled appointment (`bayId` — the current one to change only the time —, `start`, `end`) | 200, 400, 404, 409, 412, 422 |
| `POST /api/appointments/{id}/check-in` | The vehicle has arrived: opens a draft work order (`mileageKm?`, `complaint?`, body optional) and answers with `appointment` and `workOrder` | 200, 400, 404, 409, 412, 422 |
| `POST /api/appointments/{id}/cancel` | Call off a scheduled appointment (`reason`); the slot is free again | 200, 400, 404, 409, 412, 422 |
| `POST /api/appointments/{id}/no-show` | The vehicle did not arrive; possible once the slot has begun | 200, 404, 409, 412, 422 |
| `GET /api/schedule?date=2026-11-20&timeZone=Europe/Berlin` | The day's plan: every bay by name with its appointments in start order (cancelled ones left out) | 200, 400 |

`start` and `end` are ISO 8601 timestamps with an offset (`2026-11-20T09:00:00+01:00`)
and come back in UTC; slots start and end on a quarter hour, end after they
start, last at most five days and are not in the past (422 otherwise). A bay
out of service takes no bookings (`appointment.bay_out_of_service`), and a
bay or vehicle already booked for part of the slot is
`appointment.bay_already_booked` / `appointment.vehicle_already_booked` (all
409, naming the appointment in the way). Back-to-back slots (one ends at
10:00, the next starts at 10:00) do not clash.

Status flow: `Scheduled → CheckedIn`, `Scheduled → Cancelled`,
`Scheduled → NoShow`; only a scheduled appointment can be moved or settled
(422 otherwise). Checking in is the intake: the work order it opens belongs
to the vehicle's current owner and takes the appointment's purpose as its
complaint unless one is given.

The schedule reads `date` in the given IANA time zone (default `UTC`) and
answers with the UTC `start` and `end` of that day — 23 or 25 hours apart when
clocks change — so an appointment that runs past midnight shows on both days.
Bays out of service are listed with `isInService: false`; an unknown zone is
`schedule.unknown_time_zone` (400).

```bash
curl -s localhost:5080/api/vehicles/<vehicle-id>/appointments -H 'Content-Type: application/json' \
  -d '{"bayId":"<bay-id>","start":"2026-11-20T09:00:00+01:00","end":"2026-11-20T10:30:00+01:00","purpose":"Annual service"}'
curl -s localhost:5080/api/appointments/<appointment-id>/reschedule -H 'Content-Type: application/json' \
  -d '{"bayId":"<bay-id>","start":"2026-11-20T13:00:00+01:00","end":"2026-11-20T14:30:00+01:00"}'
curl -s 'localhost:5080/api/schedule?date=2026-11-20&timeZone=Europe/Berlin'
curl -s localhost:5080/api/appointments/<appointment-id>/check-in -H 'Content-Type: application/json' -d '{"mileageKm":61480}'
```

### Invoices API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `POST /api/work-orders/{workOrderId}/invoice` | Bill a completed work order: next number of the year, configured VAT rate and payment term; the work order becomes `Invoiced` | 201 + `Location`, 404, 409, 422 |
| `GET /api/work-orders/{workOrderId}/invoice` | The invoice of a work order (`invoice.not_issued` if there is none yet) | 200, 404 |
| `GET /api/invoices?status=&customerId=&page=1&pageSize=20` | Invoices, newest number first; `status` is `Issued`, `PartiallyPaid` or `Paid` | 200, 400 |
| `GET /api/invoices/{id}` | One invoice with lines, totals, payments, balance and `isOverdue` | 200, 404 |
| `GET /api/invoices/{id}/pdf` | Download the invoice as `invoice-2026-00042.pdf` | 200, 404 |
| `POST /api/invoices/{id}/payments` | Record money received (`amount`, `method`: `Cash`/`Card`/`BankTransfer`, `receivedAt?`) | 200, 400, 404, 409, 412, 422 |

An invoice is a snapshot of the work order, the customer's name and the
vehicle at the time of issue and never changes afterwards. Line totals and
`netTotal` are net; VAT is calculated once on the net total; `grossTotal`,
payments and `balance` include VAT. Numbers are consecutive per calendar
year (`2026-00001`, `2026-00002`, …); two invoices issued at the very same
moment may collide on a number, and the loser gets 409 `conflict.duplicate`
and can simply retry. A payment may not exceed the balance (422).

The VAT rate, payment term and what is printed as the seller come from the
`Invoicing` configuration section (`appsettings.json`, or environment
variables such as `Invoicing__VatRatePercent=7`); invalid values stop the API
at startup. Changing them never alters invoices already issued.

```bash
curl -s -X POST localhost:5080/api/work-orders/<work-order-id>/invoice
curl -s localhost:5080/api/invoices/<invoice-id>/payments -H 'Content-Type: application/json' -d '{"amount":100,"method":"Cash"}'
curl -s -o invoice.pdf localhost:5080/api/invoices/<invoice-id>/pdf
```

The endpoint tables above are pinned by
`OpenApiContractTests` against `/openapi/v1.json`; change both together.

### Errors

Every error is an RFC 9457 problem response (`application/problem+json`) with
`status`, `title`, `detail`, `instance` (the request path), `traceId` and a
stable `code` to switch on:

| Status | `code` | When |
| --- | --- | --- |
| 400 | `validation.failed` (+ `errors` per field) | body fields missing, blank, too long or out of range |
| 400 | `request.malformed` | unreadable JSON, wrong value type, unparsable query/route value |
| 400 | `paging.invalid_page`, `paging.invalid_page_size`, `schedule.unknown_time_zone`, `invoice.unknown_status`, `invoice.unknown_payment_method`, `user.unknown_role`, `auth.wrong_current_password` | paging out of range, unknown time zone, status, payment method or role, wrong current password |
| 401, 403 | `auth.invalid_credentials`, `auth.unauthenticated`, `auth.forbidden` | wrong sign-in, missing or invalid token, role not allowed |
| 404 | `customer.not_found`, `vehicle.not_found`, `work_order.not_found`, `work_order.line_not_found`, `bay.not_found`, `appointment.not_found`, `invoice.not_found`, `invoice.not_issued`, `user.not_found` / `route.not_found` | unknown resource / unknown URL |
| 405, 415 | `request.method_not_allowed`, `request.unsupported_media_type` | wrong method, non-JSON body |
| 409 | `customer.has_vehicles`, `customer.has_work_orders`, `customer.has_appointments`, `vehicle.duplicate_vin`, `bay.duplicate_name`, `user.duplicate_email`, `user.last_owner`, `user.cannot_deactivate_self`, `appointment.bay_out_of_service`, `appointment.bay_already_booked`, `appointment.vehicle_already_booked`, `conflict.duplicate`, `conflict.concurrent_update` | conflicts |
| 412 | `request.precondition_failed` (+ `currentETag`) | `If-Match` names a version that is no longer current |
| 422 | `domain.rule_violated` | business rule broken |
| 500 | `server.error` | unexpected failure (no details leaked) |

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Invalid request",
  "status": 400,
  "detail": "One or more fields are missing or invalid.",
  "instance": "/api/customers",
  "errors": { "email": ["'email' is required."] },
  "code": "validation.failed",
  "traceId": "00-…"
}
```

## Project layout

```
src/
  AutoFlow.Domain/        entities, value objects, business rules (no dependencies)
  AutoFlow.Application/   use cases, repository contracts, Result/Error
  AutoFlow.Infrastructure/ EF Core + PostgreSQL: DbContext, mappings, repositories
  AutoFlow.Api/           ASP.NET Core host, HTTP endpoints, authentication, error handling
web/                      React + TypeScript client (Vite), API types generated from openapi.json
tests/
  AutoFlow.Domain.Tests/        unit tests for domain rules
  AutoFlow.Application.Tests/   use-case tests against in-memory fakes
  AutoFlow.Infrastructure.Tests/ persistence tests against PostgreSQL (Testcontainers)
  AutoFlow.Api.Tests/      HTTP tests of the full stack (WebApplicationFactory + PostgreSQL):
                           endpoints, OpenAPI contract, simultaneous requests, workflows
docker-compose.yml   PostgreSQL for development, or the whole stack with the demo workshop
docs/
  architecture.md    layers, dependency rules, conventions
  adr/               architecture decision records
```

## License

Released under the [MIT License](LICENSE).
