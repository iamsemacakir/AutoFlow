# ADR 0008: Request validation and one shape for every problem response

- Status: Accepted
- Date: 2026-10-26

## Context

Until now every input check lived in the domain: a missing first name or a
negative mileage surfaced as a `DomainException` and answered 422 with only a
human-readable `detail`, one rule at a time. Some failures were silent or
inconsistent:

- a missing number (`mileageKm`, `modelYear`) arrived as `0`, so an empty
  `PUT /mileage` body tried to record a reading of zero;
- malformed JSON, a value of the wrong type or a non-JSON body produced
  framework responses whose shape depended on the environment
  (`ThrowOnBadRequest` defaults to true only in Development);
- an unmatched route or method answered 404/405 with an empty body;
- only use-case errors and write conflicts carried a stable `code`.

The React client (milestone 8) needs field-level messages for its forms and
one error shape to handle.

## Decision

**Two layers of input checks, with different statuses.**

| Check | Where | Status | `code` |
| --- | --- | --- | --- |
| Request shape: field present, not blank, length, simple lower bounds, non-empty id | request record (`IValidatableRequest`) | 400 with `errors` per field | `validation.failed` |
| Format and business rules: VIN check digit, e-mail syntax, model year not in the future, mileage never decreasing | domain (`DomainException`) | 422 | `domain.rule_violated` |

- Request records implement `IValidatableRequest.Validate(ValidationErrors)`.
  A group-level endpoint filter (`WithRequestValidation()`) runs it for every
  body argument and short-circuits with `Results.ValidationProblem`, listing
  **all** field errors keyed by their JSON (camelCase) names. Handlers only
  see well-formed requests.
- Limits come from the domain's constants (`Customer.NameMaxLength`,
  `Vehicle.MakeModelMaxLength`, `Vehicle.EarliestModelYear`), so the two
  layers cannot drift apart. Normalizing formats (VIN, plate, e-mail) is left
  to the value objects; the API does not re-implement them.
- Numeric and id fields in request records are nullable (`int?`, `Guid?`) so
  that "missing" is distinguishable from `0`.

**One problem shape.** `ProblemDetailsOptions.CustomizeProblemDetails`
(`ProblemDetailsConventions.Apply`) runs for every problem the API writes —
use-case errors, exception handlers, validation, status-code pages — and adds
`instance` (the request path), `traceId`, and a `code` chosen from the status
(`ProblemCodes.ForStatus`) when the writer did not set one.

- `RouteHandlerOptions.ThrowOnBadRequest` is always on, and
  `BadRequestExceptionHandler` maps `BadHttpRequestException` to 400
  `request.malformed` or 415 `request.unsupported_media_type`, in every
  environment, without echoing parser internals.
- `UseStatusCodePages()` turns body-less 404/405 responses into problems
  (`route.not_found`, `request.method_not_allowed`).

## Alternatives considered

- **.NET 10 built-in minimal-API validation (`AddValidation()` with
  DataAnnotations).** Idiomatic, but attribute-driven, source-generated and
  harder to keep aligned with the domain constants and our `code` extension.
  The explicit `Validate` method is a few lines per request, trivially unit
  tested and easy to read. Revisit if requests grow numerous.
- **FluentValidation.** A package and a second rule language for a handful of
  presence and length checks; not worth it yet.
- **Validate everything only in the domain.** Keeps one place for rules, but
  stops at the first failure and cannot tell a client which field is wrong.
- **Treat all input failures as 400.** 422 for a well-formed request that
  breaks a business rule lets clients separate "fix the form" from "this is
  not allowed" (for example a lower odometer reading).

## Consequences

- Clients can rely on `status`, `title`, `instance`, `code` and `traceId` on
  every error, plus `errors` for 400 `validation.failed`.
- Some limits are checked twice (request and domain); the domain stays the
  authority and still protects every other caller of the use cases.
- New request records must implement `IValidatableRequest` and their groups
  must call `WithRequestValidation()`; the architecture notes say so.
