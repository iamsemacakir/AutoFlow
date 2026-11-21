import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { readSession } from "../auth/session";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { AccountPage } from "./AccountPage";

function renderAccount() {
  return renderAt(
    <Routes>
      <Route path="/account" element={<AccountPage />} />
    </Routes>,
    { role: "Mechanic", path: "/account" },
  );
}

async function changePassword(current: string, next: string) {
  await userEvent.type(await screen.findByLabelText("Current password"), current);
  await userEvent.type(screen.getByLabelText(/^New password/), next);
  await userEvent.click(screen.getByRole("button", { name: "Change password" }));
}

describe("AccountPage", () => {
  it("changes the password and signs in again, since the change ends every session", async () => {
    const requests: { path: string; body: unknown }[] = [];
    mockApi(async (request) => {
      requests.push({ path: pathOf(request), body: await request.json() });
      if (pathOf(request) === "/api/auth/me/password") {
        return new Response(null, { status: 204 });
      }
      return json({
        accessToken: "token-after-the-change",
        tokenType: "Bearer",
        expiresAt: "2999-01-01T00:00:00Z",
        user: readSession()!.user,
      });
    });
    renderAccount();

    await changePassword("the old password", "a brand new password");

    expect(await screen.findByRole("status")).toHaveTextContent("Every other session has been signed out.");
    expect(requests).toEqual([
      {
        path: "/api/auth/me/password",
        body: { currentPassword: "the old password", newPassword: "a brand new password" },
      },
      {
        path: "/api/auth/token",
        body: { email: "mechanic@example.com", password: "a brand new password" },
      },
    ]);
    expect(readSession()?.accessToken).toBe("token-after-the-change");
    expect(screen.getByLabelText(/^New password/)).toHaveValue("");
  });

  it("keeps the session when the current password is wrong", async () => {
    const fetchMock = mockApi(() =>
      json({ status: 400, code: "auth.wrong_current_password", title: "The current password is wrong." }, 400),
    );
    renderAccount();

    await changePassword("not my password", "a brand new password");

    expect(await screen.findByRole("alert")).toHaveTextContent("The current password is wrong.");
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(readSession()?.accessToken).toBe("token-for-Mechanic");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});
