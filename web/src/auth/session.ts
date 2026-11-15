import type { SignedIn, UserDetails } from "../api/types";

const STORAGE_KEY = "autoflow.session";

export interface Session {
  accessToken: string;
  expiresAt: string;
  user: UserDetails;
}

// Kept in memory as well, so a browser that refuses storage still keeps the tab signed in.
let current: Session | null = null;

/** The session of this tab or a stored one, if it has not expired. */
export function readSession(now: Date = new Date()): Session | null {
  const session = current ?? load();
  if (!session || !session.accessToken || new Date(session.expiresAt) <= now) {
    clearSession();
    return null;
  }

  current = session;
  return session;
}

export function saveSession(signedIn: SignedIn): Session {
  current = { accessToken: signedIn.accessToken, expiresAt: signedIn.expiresAt, user: signedIn.user };
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(current));
  } catch {
    // Private mode or full storage: the session lasts until the tab is closed.
  }

  return current;
}

export function clearSession(): void {
  current = null;
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Nothing stored, nothing to clear.
  }
}

function load(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as Session) : null;
  } catch {
    return null;
  }
}
