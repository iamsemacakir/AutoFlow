# ADR 0009: Pin the HTTP contract through the OpenAPI document and test races by invariants

- Status: Accepted
- Date: 2026-10-27

## Context

Milestone 3 ends with the customer and vehicle API complete. Each endpoint
already has HTTP tests for its own success and failure cases, but three
things were not covered:

- **The published contract.** The React client (milestone 8) will be
  generated from the OpenAPI document. Nothing failed if an endpoint lost its
  `operationId`, a documented status, or its `application/problem+json`
  error responses — or if a new endpoint appeared undocumented.
- **Races through the whole stack.** ADR 0005 maps lost write races to 409,
  and the infrastructure tests provoke them deterministically one layer down.
  Whether simultaneous HTTP requests really end in 201/200 + 409 (and never
  500) was only argued, not tested.
- **Cross-resource consistency.** No test followed a realistic sequence of
  calls across customers and vehicles using only what the API returns.

## Decision

1. **Contract tests read `/openapi/v1.json`** (served in Development, which is
   the `WebApplicationFactory` default; no database needed) and compare it with
   a table of `operationId → method, path, statuses`. The document must list
   exactly the operations in the table, each with a summary and a tag, every
   4xx/5xx response as `application/problem+json`, and the read models with
   their camelCase properties. Paths are compared without a trailing slash
   (a group mapped at `/` yields `/api/customers/`). The table mirrors the
   README endpoint tables; changing the contract means changing all three.
   We compare a table rather than a full snapshot of the JSON so that
   generator details (schema layout, 3.0 vs 3.1 nullability) do not break the
   build while everything a client depends on is still pinned.
2. **Race tests assert invariants, not interleavings.** N requests are sent
   at once with `Task.WhenAll`; the scheduler decides the order. The tests
   check what must hold for every order: exactly one 201 for a VIN, losers
   answered with a documented code (`vehicle.duplicate_vin` from the pre-check
   or `conflict.duplicate` from the unique index; `conflict.concurrent_update`
   from the `xmin` token; `domain.rule_violated` for a reading that is no
   longer the highest), no other status, and a final state equal to the last
   accepted write. A test that cannot fail on a lucky interleaving would prove
   nothing, and one that needs a specific interleaving would be flaky.
3. **Workflow tests tell a front-desk story through HTTP only**: follow
   `Location` headers, read what was written, and check the other resources
   after each step (register, search, record mileage, refuse to delete an
   owner, transfer, delete the former owner).

## Consequences

- An endpoint added for work orders must be added to the contract table, or
  the suite fails — which is the point.
- Race tests are probabilistic in what they *exercise* but deterministic in
  what they *assert*; a run in which every request happens to serialize still
  passes, and a run that does race cannot produce a 500 unnoticed.
- The OpenAPI document is still only served in Development. Publishing it in
  other environments is decided with authentication (milestone 7).
