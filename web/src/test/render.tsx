import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import type { Role } from "../api/types";
import { AuthProvider } from "../auth/AuthContext";
import { saveSession } from "../auth/session";

/** Renders inside the app's providers at `path`, optionally signed in with `role`. */
export function renderAt(ui: ReactNode, { path = "/", role }: { path?: string; role?: Role } = {}) {
  if (role) {
    signInAs(role);
  }

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>{ui}</AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

export function signInAs(role: Role) {
  saveSession({
    accessToken: `token-for-${role}`,
    tokenType: "Bearer",
    expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    user: {
      id: "00000000-0000-0000-0000-000000000001",
      email: `${role.toLowerCase()}@example.com`,
      displayName: `Test ${role}`,
      role,
      isActive: true,
      createdAt: "2026-11-01T08:00:00Z",
      lastSignInAt: null,
    },
  });
}
