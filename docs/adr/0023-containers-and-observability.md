# ADR 0023: Containers, one-command demo and observability

- Status: Accepted
- Date: 2026-11-15

## Context

Milestone 9: AutoFlow must run somewhere other than a developer's machine,
a visitor should be able to try it with one command, and an operator must
be able to tell whether it is healthy and what it is doing.

## Decision

- **Two images.** The API is published with the .NET SDK image and runs on
  the ASP.NET Core runtime image as the non-root `app` user on port 8080.
  The web client is built with Node.js and served by nginx, which also
  forwards `/api` to the API, so the browser sees one origin (ADR 0022).
- **One Compose file for development and demo.** `docker compose up -d db`
  starts only PostgreSQL for working on the API; `docker compose up
  --build` starts database, API and web client. The API applies migrations,
  creates the initial owner and seeds the demo workshop on start
  (`Database__MigrateOnStartup`, `Auth__InitialOwner__*`,
  `Seed__DemoData`); the demo credentials and signing key in the Compose
  file are labelled as demo values and meant to be overridden.
- **Health.** `/health/live` (process up, no dependencies) and
  `/health/ready` (database answers) for orchestrators; `/health` stays the
  liveness check it was. Compose waits for readiness before starting nginx.
- **Logs are JSON** outside development, through the console logger's
  `json` formatter chosen in `appsettings.json` — no code and no extra
  package — with UTC timestamps and scopes (request and trace ids).
- **Metrics and traces with OpenTelemetry.** ASP.NET Core, HttpClient,
  runtime and Npgsql instrumentation plus a small `AutoFlow` meter for the
  business (work orders opened, appointments booked, invoices issued and
  their amount, payments by method). Export is chosen by configuration:
  OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, a Prometheus scraping
  endpoint at `/metrics` when `Metrics__Prometheus=true`. Nothing is
  exported by default, so tests and local runs stay quiet.
- **Images are built in CI** on every push; tagging `v*` publishes them to
  the GitHub Container Registry. An end-to-end job starts the Compose stack
  and drives the web client through the main flows with Playwright.

## Consequences

- `/metrics` is anonymous when enabled: it is meant to be scraped from
  inside the deployment's network, not exposed publicly.
- The demo signing key is public; any real deployment must set
  `AUTOFLOW_SIGNING_KEY` (or `Auth__SigningKey`).
- The Prometheus exporter package is still a pre-release; OTLP is the
  stable path.
