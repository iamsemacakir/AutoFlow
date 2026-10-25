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
| Request validation, API integration tests | ⏳ next |
| Work orders, appointments, invoices | ⏳ planned |
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
| `POST /api/customers` | Register a customer (`firstName`, `lastName`, `email`, `phoneNumber?`) | 201 + `Location`, 422 |
| `PUT /api/customers/{id}` | Replace name and contact details | 200, 404, 409, 422 |
| `DELETE /api/customers/{id}` | Delete a customer who owns no vehicles | 204, 404, 409 |

Errors are RFC 9457 problem responses with a stable `code`
(e.g. `customer.not_found`, `customer.has_vehicles`, `paging.invalid_page_size`).

### Vehicles API

| Method & path | Purpose | Responses |
| --- | --- | --- |
| `GET /api/customers/{customerId}/vehicles` | List a customer's vehicles, oldest registration first | 200, 404 |
| `POST /api/customers/{customerId}/vehicles` | Register a vehicle (`vin`, `licensePlate`, `make`, `model`, `modelYear`, `mileageKm`) | 201 + `Location`, 404, 409, 422 |
| `GET /api/vehicles/{id}` | Get one vehicle | 200, 404 |
| `PUT /api/vehicles/{id}/mileage` | Record an odometer reading (`mileageKm`); readings never go backwards | 200, 404, 409, 422 |
| `POST /api/vehicles/{id}/transfer` | Transfer the vehicle to another customer (`customerId`) | 200, 404, 409, 422 |

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
  AutoFlow.Api.Tests/      HTTP tests of the full stack (WebApplicationFactory + PostgreSQL)
docker-compose.yml   local PostgreSQL for development
docs/
  architecture.md    layers, dependency rules, conventions
  adr/               architecture decision records
```

## License

Not yet licensed. All rights reserved by the repository owner until a license is added.
