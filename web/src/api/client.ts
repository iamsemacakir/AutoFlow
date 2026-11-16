import createClient, { type Middleware } from "openapi-fetch";
import { clearSession, readSession } from "../auth/session";
import { ApiError } from "./errors";
import type { paths } from "./schema";

/** Fired when the API rejects the stored token, so the app can send the user to the sign-in page. */
export const SIGNED_OUT_EVENT = "autoflow:signed-out";

/**
 * The typed API client. Paths, parameters and bodies are checked against
 * `schema.d.ts`, which is generated from the API's own OpenAPI document
 * (`npm run generate:api`), so a changed contract fails the type check.
 */
export const api = createClient<paths>({
  baseUrl: globalThis.location?.origin ?? "",
  // Resolved per call, so tests can replace the global fetch.
  fetch: (request) => globalThis.fetch(request),
});

const authentication: Middleware = {
  onRequest({ request }) {
    const session = readSession();
    if (session) {
      request.headers.set("Authorization", `Bearer ${session.accessToken}`);
    }

    return request;
  },
  onResponse({ request, response }) {
    if (response.status === 401 && !request.url.endsWith("/api/auth/token")) {
      clearSession();
      globalThis.dispatchEvent(new Event(SIGNED_OUT_EVENT));
    }

    return response;
  },
};

/**
 * The last `ETag` the API sent per resource (`/api/customers/{id}`), from a
 * read or a change. Sending it back as `If-Match` makes the API refuse a
 * change with 412 when someone else changed the resource since this client
 * loaded it, instead of overwriting their change (ADR 0024).
 */
const etags = new Map<string, string>();

/** `/api/work-orders/{id}/approve` → `/api/work-orders/{id}`: the resource a tag belongs to. */
export function resourceOf(path: string): string | null {
  return /^\/api\/[a-z-]+\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i.exec(path)?.[0] ?? null;
}

const versions: Middleware = {
  onResponse({ request, response }) {
    const resource = resourceOf(new URL(request.url).pathname);
    const etag = response.headers.get("ETag");
    if (resource && etag && response.ok) {
      etags.set(resource, etag);
    }

    return response;
  },
};

api.use(authentication);
api.use(versions);

/** The `If-Match` header for a change to `resource`, if this client has seen a version of it. */
export function ifMatch(resource: string): Record<string, string> {
  const etag = etags.get(resource);
  return etag ? { "If-Match": etag } : {};
}

/** Whether a change was refused because the resource changed since it was loaded. */
export function isStale(error: unknown): boolean {
  return error instanceof ApiError && error.status === 412;
}

/**
 * The data of a successful answer, or an {@link ApiError} for an error
 * answer, so React Query sees failures as thrown errors.
 */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (!result.response.ok) {
    throw ApiError.from(result.response.status, result.error);
  }

  return result.data as T;
}

/** Downloads a binary answer (the invoice PDF) with the user's token and saves it under the server's file name. */
export async function download(path: string, fallbackName: string): Promise<void> {
  const session = readSession();
  const response = await globalThis.fetch(path, {
    headers: session ? { Authorization: `Bearer ${session.accessToken}` } : {},
  });

  if (!response.ok) {
    throw ApiError.from(response.status, await response.json().catch(() => null));
  }

  const disposition = response.headers.get("Content-Disposition") ?? "";
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? fallbackName;
  const url = URL.createObjectURL(await response.blob());
  const link = Object.assign(document.createElement("a"), { href: url, download: decodeURIComponent(name) });
  link.click();
  URL.revokeObjectURL(url);
}
