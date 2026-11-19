import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, ifMatch, isStale, unwrap } from "../api/client";
import type { Role, UserDetails } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { Card, ErrorMessage, Field, Loading, PageHeader } from "../components/ui";
import { formatDateTime, humanize } from "../lib/format";

const roles: Role[] = ["Owner", "ServiceAdvisor", "Mechanic"];

export function StaffPage() {
  const queryClient = useQueryClient();
  const users = useQuery({ queryKey: ["users"], queryFn: async () => unwrap(await api.GET("/api/users")) });
  const [form, setForm] = useState({ email: "", displayName: "", role: "Mechanic", password: "" });
  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST("/api/users", { body: form })),
    onSuccess: async () => {
      setForm({ email: "", displayName: "", role: "Mechanic", password: "" });
      await queryClient.invalidateQueries({ queryKey: ["users"] });
    },
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });

  return (
    <>
      <PageHeader title="Staff" subtitle="Accounts are deactivated, never deleted." />
      <ErrorMessage error={users.error} />
      {users.isPending && <Loading />}
      {users.data && (
        <Card>
          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>E-mail</th>
                <th>Role</th>
                <th>Last sign-in</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {users.data.map((user) => (
                <UserRow key={user.id} user={user} />
              ))}
            </tbody>
          </table>
        </Card>
      )}
      <Card title="New account">
        <form
          className="form-grid"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <Field label="Name">
            <input required value={form.displayName} onChange={set("displayName")} />
          </Field>
          <Field label="E-mail">
            <input type="email" required value={form.email} onChange={set("email")} />
          </Field>
          <Field label="Role">
            <select value={form.role} onChange={set("role")}>
              {roles.map((role) => (
                <option key={role} value={role}>
                  {humanize(role)}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Initial password" hint="At least 10 characters.">
            <input type="password" required minLength={10} autoComplete="new-password" value={form.password} onChange={set("password")} />
          </Field>
          <div className="actions">
            <button type="submit" className="primary" disabled={create.isPending}>
              Open account
            </button>
          </div>
        </form>
        <ErrorMessage error={create.error} />
      </Card>
    </>
  );
}

/**
 * One staff account with the owner's actions. The list carries no versions
 * (ADR 0024), so the row loads its account: that shows its current role and
 * state and gives the version that a role change, deactivation and
 * reactivation send as `If-Match`. Until it has arrived, the actions wait.
 */
function UserRow({ user: listed }: { user: UserDetails }) {
  const { user: me } = useAuth();
  const queryClient = useQueryClient();
  const current = useQuery({
    queryKey: ["user", listed.id],
    queryFn: async () => unwrap(await api.GET("/api/users/{id}", { params: { path: { id: listed.id } } })),
    placeholderData: listed,
  });
  const user = current.data ?? listed;
  const loaded = !current.isPlaceholderData && current.isSuccess;
  const path = { params: { path: { id: user.id } } };
  const headers = () => ifMatch(`/api/users/${user.id}`);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["users"] });
  const changed = async (next: UserDetails) => {
    queryClient.setQueryData(["user", user.id], next);
    await refresh();
  };
  // Another owner changed the account since the page was loaded: show it as it is now.
  const reloadIfStale = (error: unknown) =>
    isStale(error) ? Promise.all([queryClient.invalidateQueries({ queryKey: ["user", user.id] }), refresh()]) : undefined;

  const changeRole = useMutation({
    mutationFn: async (role: string) =>
      unwrap(await api.PUT("/api/users/{id}/role", { ...path, body: { role }, headers: headers() })),
    onSuccess: changed,
    onError: reloadIfStale,
  });
  const toggle = useMutation({
    mutationFn: async () =>
      unwrap(
        user.isActive
          ? await api.POST("/api/users/{id}/deactivate", { ...path, headers: headers() })
          : await api.POST("/api/users/{id}/reactivate", { ...path, headers: headers() }),
      ),
    onSuccess: changed,
    onError: reloadIfStale,
  });

  return (
    <tr className={user.isActive ? undefined : "inactive"}>
      <td>{user.displayName}</td>
      <td>{user.email}</td>
      <td>
        <select
          value={user.role}
          onChange={(e) => changeRole.mutate(e.target.value)}
          disabled={!loaded || changeRole.isPending}
          aria-label={`Role of ${user.displayName}`}
        >
          {roles.map((role) => (
            <option key={role} value={role}>
              {humanize(role)}
            </option>
          ))}
        </select>
        <ErrorMessage error={changeRole.error ?? toggle.error ?? current.error} />
      </td>
      <td>{user.lastSignInAt ? formatDateTime(user.lastSignInAt) : "never"}</td>
      <td className="number">
        {user.id !== me?.id && (
          <button type="button" className={user.isActive ? "danger" : undefined} onClick={() => toggle.mutate()} disabled={!loaded || toggle.isPending}>
            {user.isActive ? "Deactivate" : "Reactivate"}
          </button>
        )}
      </td>
    </tr>
  );
}
