import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import { api, unwrap } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { Card, ErrorMessage, Field, PageHeader } from "../components/ui";
import { humanize } from "../lib/format";

export function AccountPage() {
  const { user, signIn } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const change = useMutation({
    mutationFn: async () => {
      unwrap(await api.POST("/api/auth/me/password", { body: { currentPassword, newPassword } }));
      // The change ends every session, this one included (ADR 0025): sign in again with the new password.
      if (user) {
        await signIn(user.email, newPassword);
      }
    },
    onSuccess: () => {
      setCurrentPassword("");
      setNewPassword("");
    },
  });

  return (
    <>
      <PageHeader title={user?.displayName ?? "Account"} subtitle={user ? `${user.email} · ${humanize(user.role)}` : undefined} />
      <Card title="Change password">
        <form
          className="form-grid"
          onSubmit={(e) => {
            e.preventDefault();
            change.mutate();
          }}
        >
          <Field label="Current password">
            <input type="password" required autoComplete="current-password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
          </Field>
          <Field label="New password" hint="At least 10 characters.">
            <input type="password" required minLength={10} autoComplete="new-password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
          </Field>
          <div className="actions">
            <button type="submit" className="primary" disabled={change.isPending}>
              Change password
            </button>
          </div>
        </form>
        {change.isSuccess && (
          <p className="success" role="status">
            Your password has been changed. Every other session has been signed out.
          </p>
        )}
        <ErrorMessage error={change.error} />
      </Card>
    </>
  );
}
