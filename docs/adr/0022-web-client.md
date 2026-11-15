# ADR 0022: Web client — React, TypeScript and types generated from the API

- Status: Accepted
- Date: 2026-11-15

## Context

Milestone 8 adds the screens the staff work with: the day's schedule,
customers and their vehicles, work orders, invoices, bays and accounts. The
API is the only backend; its contract is already pinned by the OpenAPI
contract tests (ADR 0009).

## Decision

- **React + TypeScript, built with Vite**, in `web/`. A single-page app:
  React Router for the screens, TanStack Query for server state (caching,
  refetching after a change, no hand-written loading flags), plain CSS with
  a handful of variables instead of a component library. Strict TypeScript
  (`strict`, `noUncheckedIndexedAccess`).
- **The API types are generated, never written by hand.** `web/openapi.json`
  is a committed copy of the API's OpenAPI document; `npm run generate:api`
  turns it into `src/api/schema.d.ts` (openapi-typescript), and
  `openapi-fetch` checks every path, parameter and body against it. Two
  checks keep the chain closed: an API test fails when the live document
  differs from the copy, and CI fails when the generated types differ from
  what the copy produces. A contract change therefore shows up as a type
  error in the client in the same pull request. JSON numbers are strict in
  the API, so amounts are `number`, not `number | string`.
- **One origin.** The browser only talks to the server that served the
  page; Vite's dev server and nginx in production forward `/api` to the
  API. No CORS configuration, and the token never crosses origins.
- **The token lives in memory and `localStorage`** with its expiry, and is
  sent as a bearer header by a client middleware. A 401 from the API signs
  the user out everywhere in the app. Screens and actions are hidden by role
  for convenience; the API enforces the roles (ADR 0021).
- **Times are shown in the browser's time zone**, and the schedule asks the
  API for the day in that zone, so "today" is the workshop's today.
- **Tests run in jsdom with Vitest and Testing Library** against a mocked
  `fetch` that answers like the API, so components are tested through what
  the user sees and clicks.

## Consequences

- Adding a field to an API response needs `AUTOFLOW_UPDATE_OPENAPI=1` once
  in the API tests and `npm run generate:api` in `web/`; forgetting either
  fails CI with a message saying which.
- An XSS hole in the client could read the token from storage; the client
  renders no HTML from data (React escapes everything), and the token's
  lifetime is a working day.
