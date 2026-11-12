# AutoFlow

[![CI](https://github.com/iamsemacakir/AutoFlow/actions/workflows/ci.yml/badge.svg)](https://github.com/iamsemacakir/AutoFlow/actions/workflows/ci.yml)

AutoFlow is a management system for independent car workshops: customers,
their vehicles, work orders, appointments and invoices — built with
**C# / .NET 10** and **ASP.NET Core**, with PostgreSQL and a React + TypeScript
front end planned.

## Status

Early foundation. See [ROADMAP.md](ROADMAP.md) for what is done and what comes next.

| Area | State |
| --- | --- |
| Domain model: Customer, Vehicle (VIN, plate, mileage) | ✅ |
| Application layer: use cases, repository contracts, result type | ✅ |
| API host: health check, problem details, OpenAPI | ✅ |
| Unit + integration tests, CI | ✅ |
| Persistence: EF Core + PostgreSQL mappings, repositories | ✅ |
| Database migrations, docker-compose for local PostgreSQL | ✅ |
| API composition, 409 for write conflicts | ✅ |
| Customer endpoints: CRUD, paging, search | ✅ |
| Vehicle endpoints: register, list per customer, mileage, transfer | ✅ |
| Request validation, consistent problem responses | ✅ |
| API integration tests: OpenAPI contract, races, workflows | ✅ |
| Work orders: domain aggregate, status flow, labour/part lines | ✅ |
| Work orders: persistence (lines table, migration, concurrency) | ✅ |
| Work orders: open use case (intake reading, current owner) | ✅ |
| Work orders: add/remove labour and part lines use cases | ✅ |
| Work orders: approve, start, complete, cancel use cases | ✅ |
| Work orders: get, paged history per vehicle | ✅ |
| Work orders: open, get, history endpoints | ✅ |
| Work orders: add/remove line endpoints | ✅ |
| Work orders: approve, start, complete, cancel endpoints | ✅ |
| Appointments: bays (capacity) and 15-minute slots with overlap rule | ✅ |
| Appointments: aggregate (book, reschedule, check in, cancel, no-show, double-booking rules) | ✅ |
| Appointments: bay persistence (table, unique name, migration) | ✅ |
| Appointments: appointment persistence (slot columns, exclusion constraints against double booking) | ✅ |
| Appointments: bay use cases (create, rename, out of / back in service, get, list) | ✅ |
| Appointments: book, reschedule, get use cases (bay in service, clash checks) | ✅ |
| Appointments: check-in (opens the work order), cancel, no-show use cases | ✅ |
| Appointments: day schedule use case, endpoints | ⏳ next |
| Invoices | ⏳ planned |
| Auth, UI, deployment | ⏳ planned |

## Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), and Docker
for the infrastructure and API tests (they start throw-away PostgreSQL containers).

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/AutoFlow.Api   # http://localhost:5080
```

### Local database

`docker-compose.yml` starts PostgreSQL 17 on `localhost:5432`
(database, user and password `autoflow`). The schema is managed by EF Core
migrations; the `dotnet ef` tool is pinned in `.config/dotnet-tools.json`.

```bash
docker compose up -d               # start PostgreSQL
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

### Customers API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `GET /api/customers?search=&page=1&pageSize=20` | List customers by name; every search term must match first name, last name, e-mail or phone | 200, 400 |
| `GET /api/customers/{id}` | Get one customer | 200, 404 |
| `POST /api/customers` | Register a customer (`firstName`, `lastName`, `email`, `phoneNumber?`) | 201 + `Location`, 400, 422 |
| `PUT /api/customers/{id}` | Replace name and contact details | 200, 400, 404, 409, 422 |
| `DELETE /api/customers/{id}` | Delete a customer who owns no vehicles and has no work orders | 204, 404, 409 |

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
| `PUT /api/vehicles/{id}/mileage` | Record an odometer reading (`mileageKm`); readings never go backwards | 200, 400, 404, 409, 422 |
| `POST /api/vehicles/{id}/transfer` | Transfer the vehicle to another customer (`customerId`) | 200, 400, 404, 409, 422 |

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
| `POST /api/work-orders/{id}/labour-lines` | Add labour (`description`, `hours`, `hourlyRate`) | 200, 400, 404, 409, 422 |
| `POST /api/work-orders/{id}/part-lines` | Add a part (`partNumber?`, `description`, `quantity`, `unitPrice`) | 200, 400, 404, 409, 422 |
| `DELETE /api/work-orders/{id}/lines/{lineId}` | Remove a line; the other lines keep their numbers | 200, 404, 409, 422 |
| `POST /api/work-orders/{id}/approve` | The customer agrees to the estimate (draft with lines); the total is kept as `approvedTotal` | 200, 404, 409, 422 |
| `POST /api/work-orders/{id}/start` | A mechanic starts work on an approved work order | 200, 404, 409, 422 |
| `POST /api/work-orders/{id}/complete` | Work is done; the lines become final | 200, 404, 409, 422 |
| `POST /api/work-orders/{id}/cancel` | Cancel a draft or approved work order (`reason`) | 200, 400, 404, 409, 422 |

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
| 400 | `paging.invalid_page`, `paging.invalid_page_size` | paging out of range |
| 404 | `customer.not_found`, `vehicle.not_found`, `work_order.not_found`, `work_order.line_not_found` / `route.not_found` | unknown resource / unknown URL |
| 405, 415 | `request.method_not_allowed`, `request.unsupported_media_type` | wrong method, non-JSON body |
| 409 | `customer.has_vehicles`, `customer.has_work_orders`, `vehicle.duplicate_vin`, `conflict.duplicate`, `conflict.concurrent_update` | conflicts |
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
  AutoFlow.Api/           ASP.NET Core host, HTTP endpoints, error handling
tests/
  AutoFlow.Domain.Tests/        unit tests for domain rules
  AutoFlow.Application.Tests/   use-case tests against in-memory fakes
  AutoFlow.Infrastructure.Tests/ persistence tests against PostgreSQL (Testcontainers)
  AutoFlow.Api.Tests/      HTTP tests of the full stack (WebApplicationFactory + PostgreSQL):
                           endpoints, OpenAPI contract, simultaneous requests, workflows
docker-compose.yml   local PostgreSQL for development
docs/
  architecture.md    layers, dependency rules, conventions
  adr/               architecture decision records
```

## License

Not yet licensed. All rights reserved by the repository owner until a license is added.
