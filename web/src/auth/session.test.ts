import { clearSession, readSession, saveSession } from "./session";

const signedIn = (expiresAt: string) => ({
  accessToken: "token",
  tokenType: "Bearer",
  expiresAt,
  user: {
    id: "1",
    email: "owner@example.com",
    displayName: "Olga Owner",
    role: "Owner",
    isActive: true,
    createdAt: "2026-11-01T08:00:00Z",
    lastSignInAt: null,
  },
});

describe("session", () => {
  afterEach(() => clearSession());

  it("keeps a session until it expires", () => {
    saveSession(signedIn("2026-11-20T18:00:00Z"));

    expect(readSession(new Date("2026-11-20T17:59:00Z"))?.user.displayName).toBe("Olga Owner");
    expect(readSession(new Date("2026-11-20T18:00:00Z"))).toBeNull();
    expect(localStorage.getItem("autoflow.session")).toBeNull();
  });

  it("forgets the session on sign-out", () => {
    saveSession(signedIn("2999-01-01T00:00:00Z"));

    clearSession();

    expect(readSession()).toBeNull();
  });
});
