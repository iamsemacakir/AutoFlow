# ADR 0025: A password change ends every session

- Status: Accepted
- Date: 2026-11-21

## Context

Access tokens are self-contained JWTs that last a working day (ADR 0021).
Every request re-reads the account, so deactivating it or changing its role
takes effect at once — but changing the password did not: a token signed in
with the old password stayed valid until it expired. The usual reason to
change a password is that someone else may know it, and that someone may
already hold a token.

## Decision

- **Tokens name the password they were signed in with.** The token carries
  a `pwd` claim: the first 16 bytes of HMAC-SHA256 over the account's
  stored password hash, keyed with the signing key (`PasswordStamp`). The
  bearer handler's account check, which already runs on every request,
  compares it with the stamp of the current hash and refuses the token
  (401 `auth.unauthenticated`) when they differ.
- **Every new password gives a new stamp.** The hasher salts each hash
  afresh, so even setting the same password again ends the old sessions.
- **No new column.** The stamp is derived from the hash the account
  already stores, so there is no migration and nothing to keep in sync; the
  keyed digest reveals nothing about the hash to whoever holds the token.
- **Every session ends, the one that changed it included.** The API keeps
  `POST /api/auth/me/password` as it is (204); a client that wants to stay
  signed in signs in again with the new password, as the web client's
  account page does.

## Consequences

- Tokens issued before this change have no `pwd` claim and are refused
  once: everybody signs in again after the upgrade.
- Rotating `Auth:SigningKey` already ended every session; it still does.
- An owner who suspects a colleague's account is misused can still
  deactivate it at once; the colleague changing the password is the way to
  end only the sessions without closing the account.
