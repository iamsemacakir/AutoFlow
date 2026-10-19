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
| API host: health check, problem details, OpenAPI | ✅ |
| Unit + integration tests, CI | ✅ |
| Persistence (EF Core + PostgreSQL) | ⏳ planned |
| Work orders, appointments, invoices | ⏳ planned |
| Auth, UI, deployment | ⏳ planned |

## Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/AutoFlow.Api   # http://localhost:5080
```

Useful endpoints while running in Development:

- `GET /health` — liveness check
- `GET /openapi/v1.json` — OpenAPI document

## Project layout

```
src/
  AutoFlow.Domain/   entities, value objects, business rules (no dependencies)
  AutoFlow.Api/      ASP.NET Core host, HTTP endpoints, error handling
tests/
  AutoFlow.Domain.Tests/   unit tests for domain rules
  AutoFlow.Api.Tests/      in-memory integration tests (WebApplicationFactory)
docs/
  architecture.md    layers, dependency rules, conventions
  adr/               architecture decision records
```

## License

Not yet licensed. All rights reserved by the repository owner until a license is added.
