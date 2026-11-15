import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, unwrap } from "../api/client";
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

function UserRow({ user }: { user: UserDetails }) {
  const { user: me } = useAuth();
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["users"] });
  const path = { params: { path: { id: user.id } } };

  const changeRole = useMutation({
    mutationFn: async (role: string) => unwrap(await api.PUT("/api/users/{id}/role", { ...path, body: { role } })),
    onSuccess: refresh,
  });
  const toggle = useMutation({
    mutationFn: async () =>
      unwrap(
        user.isActive
          ? await api.POST("/api/users/{id}/deactivate", path)
          : await api.POST("/api/users/{id}/reactivate", path),
      ),
    onSuccess: refresh,
  });

  return (
    <tr className={user.isActive ? undefined : "inactive"}>
      <td>{user.displayName}</td>
      <td>{user.email}</td>
      <td>
        <select value={user.role} onChange={(e) => changeRole.mutate(e.target.value)} aria-label={`Role of ${user.displayName}`}>
          {roles.map((role) => (
            <option key={role} value={role}>
              {humanize(role)}
            </option>
          ))}
        </select>
        <ErrorMessage error={changeRole.error ?? toggle.error} />
      </td>
      <td>{user.lastSignInAt ? formatDateTime(user.lastSignInAt) : "never"}</td>
      <td className="number">
        {user.id !== me?.id && (
          <button type="button" className={user.isActive ? "danger" : undefined} onClick={() => toggle.mutate()} disabled={toggle.isPending}>
            {user.isActive ? "Deactivate" : "Reactivate"}
          </button>
        )}
      </td>
    </tr>
  );
}
