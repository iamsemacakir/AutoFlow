import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, SIGNED_OUT_EVENT, unwrap } from "../api/client";
import type { Role, UserDetails } from "../api/types";
import { clearSession, readSession, saveSession } from "./session";

interface AuthState {
  user: UserDetails | null;
  signIn: (email: string, password: string) => Promise<UserDetails>;
  signOut: () => void;
  /** True if the signed-in user has one of the roles. */
  hasRole: (...roles: Role[]) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDetails | null>(() => readSession()?.user ?? null);

  useEffect(() => {
    // The API rejected the token (expired, account closed): back to the sign-in page.
    const onSignedOut = () => setUser(null);
    globalThis.addEventListener(SIGNED_OUT_EVENT, onSignedOut);
    return () => globalThis.removeEventListener(SIGNED_OUT_EVENT, onSignedOut);
  }, []);

  const signIn = useCallback(async (email: string, password: string) => {
    const signedIn = unwrap(await api.POST("/api/auth/token", { body: { email, password } }));
    setUser(saveSession(signedIn).user);
    return signedIn.user;
  }, []);

  const signOut = useCallback(() => {
    clearSession();
    setUser(null);
  }, []);

  const hasRole = useCallback((...roles: Role[]) => user !== null && roles.includes(user.role as Role), [user]);

  const value = useMemo(() => ({ user, signIn, signOut, hasRole }), [user, signIn, signOut, hasRole]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const state = useContext(AuthContext);
  if (!state) {
    throw new Error("useAuth must be used inside <AuthProvider>.");
  }

  return state;
}

/** Roles that may do front-desk work: customers, bookings, estimates, invoices. */
export const FRONT_DESK: Role[] = ["Owner", "ServiceAdvisor"];
