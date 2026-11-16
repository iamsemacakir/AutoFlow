import { vi } from "vitest";

/** A JSON answer as the API sends it. */
export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json", ...headers },
  });
}

/**
 * Replaces `fetch` for one test: every request the app sends goes to
 * `handler`, which answers like the API would. Returns the mock to inspect calls.
 */
export function mockApi(handler: (request: Request) => Response | Promise<Response>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
    handler(input instanceof Request ? input : new Request(new URL(String(input), "http://localhost:3000"), init)),
  );
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

export function pathOf(request: Request): string {
  const url = new URL(request.url);
  return url.pathname + url.search;
}
