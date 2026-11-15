import { NavLink, Outlet } from "react-router-dom";
import type { Role } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { humanize } from "../lib/format";

const links: { to: string; label: string; roles?: Role[] }[] = [
  { to: "/", label: "Schedule" },
  { to: "/customers", label: "Customers" },
  { to: "/invoices", label: "Invoices", roles: ["Owner", "ServiceAdvisor"] },
  { to: "/bays", label: "Bays" },
  { to: "/staff", label: "Staff", roles: ["Owner"] },
];

export function Layout() {
  const { user, signOut, hasRole } = useAuth();

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">A</span>
          AutoFlow
        </div>
        <nav aria-label="Main">
          {links
            .filter((link) => !link.roles || hasRole(...link.roles))
            .map((link) => (
              <NavLink key={link.to} to={link.to} end={link.to === "/"}>
                {link.label}
              </NavLink>
            ))}
        </nav>
        {user && (
          <div className="account">
            <NavLink to="/account" className="account-name">
              {user.displayName}
            </NavLink>
            <span className="muted">{humanize(user.role)}</span>
            <button type="button" className="link" onClick={signOut}>
              Sign out
            </button>
          </div>
        )}
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
