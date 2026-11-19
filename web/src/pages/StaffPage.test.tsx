import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { UserDetails } from "../api/types";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { StaffPage } from "./StaffPage";

const id = "7c2e9a41-5d3b-4f8e-a6c1-0b9d8e7f6a52";
const mechanic: UserDetails = {
  id,
  email: "jonas@example.com",
  displayName: "Jonas Becker",
  role: "Mechanic",
  isActive: true,
  createdAt: "2026-11-01T08:00:00Z",
  lastSignInAt: null,
};

/** One account as the API keeps it, with a version the test can bump; records each change's `If-Match`. */
function staffApi() {
  const state = { stored: mechanic, version: 5, changes: [] as (string | null)[] };
  const changes: Record<string, (user: UserDetails, body: unknown) => UserDetails> = {
    [`PUT /api/users/${id}/role`]: (user, body) => ({ ...user, role: (body as { role: string }).role }),
    [`POST /api/users/${id}/deactivate`]: (user) => ({ ...user, isActive: false }),
    [`POST /api/users/${id}/reactivate`]: (user) => ({ ...user, isActive: true }),
  };
  mockApi(async (request) => {
    const change = changes[`${request.method} ${pathOf(request)}`];
    if (change) {
      state.changes.push(request.headers.get("If-Match"));
      if (request.headers.get("If-Match") !== `"${state.version}"`) {
        return json({ status: 412, code: "request.precondition_failed", title: "Precondition failed" }, 412);
      }
      const body: unknown = request.method === "PUT" ? await request.json() : null;
      state.stored = change(state.stored, body);
      state.version += 1;
      return json(state.stored, 200, { ETag: `"${state.version}"` });
    }
    if (pathOf(request) === `/api/users/${id}`) {
      return json(state.stored, 200, { ETag: `"${state.version}"` });
    }
    return json([state.stored]);
  });
  renderAt(<StaffPage />, { role: "Owner", path: "/staff" });
  return state;
}

/** The role select once the account's version has arrived. */
async function roleSelect() {
  const select = await screen.findByLabelText("Role of Jonas Becker");
  await expect.poll(() => (select as HTMLSelectElement).disabled).toBe(false);
  return select;
}

describe("StaffPage", () => {
  it("changes a role against the version it loaded", async () => {
    const api = staffApi();

    const select = await roleSelect();
    await userEvent.selectOptions(select, "ServiceAdvisor");

    await expect.poll(() => api.stored.role).toBe("ServiceAdvisor");
    expect(select).toHaveValue("ServiceAdvisor");
    expect(api.changes).toEqual(['"5"']);
  });

  it("deactivates and reactivates with the version of each answer", async () => {
    const api = staffApi();
    await roleSelect();

    await userEvent.click(screen.getByRole("button", { name: "Deactivate" }));
    await userEvent.click(await screen.findByRole("button", { name: "Reactivate" }));

    expect(await screen.findByRole("button", { name: "Deactivate" })).toBeInTheDocument();
    expect(api.changes).toEqual(['"5"', '"6"']);
  });

  it("shows the account as it is now when another owner changed it in the meantime", async () => {
    const api = staffApi();
    const select = await roleSelect();
    // Another owner has just deactivated the account.
    api.stored = { ...api.stored, isActive: false };
    api.version += 1;

    await userEvent.selectOptions(select, "Owner");

    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
    expect(await screen.findByRole("button", { name: "Reactivate" })).toBeInTheDocument();
    expect(screen.getByLabelText("Role of Jonas Becker")).toHaveValue("Mechanic");
    expect(api.stored.role).toBe("Mechanic");
    expect(api.changes).toEqual(['"5"']);
  });
});
