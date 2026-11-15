# ADR 0021: Staff accounts, bearer tokens and role policies

- Status: Accepted
- Date: 2026-11-15

## Context

AutoFlow is used by a workshop's own staff: an owner, service advisors at
the front desk, mechanics on the floor. Requests must be tied to a person,
what a person may do depends on their job, and someone leaving the company
must lose access at once. The React client (milestone 8) calls the API from
the browser; there is no single sign-on provider to integrate with.

## Decision

- **Accounts are a domain concept.** A `User` has an e-mail login, a display
  name, one `UserRole` (`Owner`, `ServiceAdvisor`, `Mechanic`), a password
  hash and an active flag. Accounts are deactivated, never deleted. The
  domain checks the password *policy* (10 to 128 characters, nothing else,
  as NIST SP 800-63B recommends) but never sees a password: hashing is the
  `IPasswordHasher` port.
- **PBKDF2-HMAC-SHA512 from the base class library**, 210,000 iterations
  (OWASP), 16-byte random salt, stored as
  `pbkdf2-sha512$iterations$salt$hash`, compared in constant time. No
  ASP.NET Core Identity: its user store, tables and UI are far more than a
  handful of staff accounts need.
- **Sign-in returns a JWT** (`POST /api/auth/token`), HMAC-SHA256 signed
  with `Auth:SigningKey` (at least 32 bytes; the API refuses to start
  without it), valid for a working day by default. Unknown address, wrong
  password and deactivated account all answer the same 401
  `auth.invalid_credentials`, and an unknown address still costs one hash
  verification, so the response does not reveal which accounts exist.
- **The token is not the source of truth.** On every request the bearer
  handler loads the account by the token's subject: a deactivated account's
  tokens fail immediately, and the role used for authorization is the one
  in the database, so a promotion or demotion takes effect on the next
  request. One primary-key lookup per request is cheap at this scale and
  avoids revocation lists and refresh tokens.
- **Abilities are named policies**, not role lists on endpoints:
  `Staff` (any active account), `FrontDesk` (owner, service advisor) and
  `ManageWorkshop` (owner). Endpoints name a policy; the mapping from roles
  to policies lives in `Policies`.
- **Owners manage accounts** at `/api/users`. The workshop always keeps an
  active owner (`user.last_owner`, 409), and nobody can deactivate
  themselves (`user.cannot_deactivate_self`, 409).
- **Bootstrapping.** On start the API can apply migrations
  (`Database:MigrateOnStartup`) and create the first owner from
  `Auth:InitialOwner` — only while no account exists, so the configured
  password cannot be used to take over a running system.
- **401 and 403 are problem responses** like every other error
  (`auth.unauthenticated`, `auth.forbidden`); the bearer handler sets the
  status and `WWW-Authenticate`, the status-code pages write the body.

## Consequences

- The browser keeps the token in memory and local storage; with an
  eight-hour lifetime a stolen token is useful for at most a shift, and
  deactivating the account ends it at once.
- Rotating `Auth:SigningKey` signs everybody out, which is the intended
  emergency switch.
- Tests sign in through the real endpoint with real hashes; the fixture
  opens one account per role once and reuses the tokens.
