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
| Customer & vehicle HTTP endpoints | ⏳ next |
| Work orders, appointments, invoices | ⏳ planned |
| Auth, UI, deployment | ⏳ planned |

## Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), and Docker
for the infrastructure tests (they start a throw-away PostgreSQL container).

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

To target another database, set `AUTOFLOW_CONNECTION_STRING` before running
`dotnet ef`. After changing a mapping, add a migration (the test suite fails
until you do):

```bash
dotnet ef migrations add <Name> --project src/AutoFlow.Infrastructure \
  --output-dir Persistence/Migrations
```

Useful endpoints while running in Development:

- `GET /health` — liveness check
- `GET /openapi/v1.json` — OpenAPI document

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
  AutoFlow.Api.Tests/      in-memory integration tests (WebApplicationFactory)
docker-compose.yml   local PostgreSQL for development
docs/
  architecture.md    layers, dependency rules, conventions
  adr/               architecture decision records
```

## License

Not yet licensed. All rights reserved by the repository owner until a license is added.
