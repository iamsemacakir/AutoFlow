import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { BayDetails } from "../api/types";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { BaysPage } from "./BaysPage";

const id = "3a1f6c2e-8b4d-4e7a-9f1c-2d5e8b7a6c40";
const lift: BayDetails = { id, name: "Lift 1", isInService: true, createdAt: "2026-11-01T08:00:00Z" };

/** One bay as the API keeps it, with a version the test can bump; records each change's `If-Match`. */
function bayApi(role: "Owner" | "Mechanic" = "Owner") {
  const state = { stored: lift, version: 3, changes: [] as (string | null)[], reads: 0 };
  const changes: Record<string, (bay: BayDetails, body: unknown) => BayDetails> = {
    [`PUT /api/bays/${id}`]: (bay, body) => ({ ...bay, name: (body as { name: string }).name }),
    [`POST /api/bays/${id}/take-out-of-service`]: (bay) => ({ ...bay, isInService: false }),
    [`POST /api/bays/${id}/return-to-service`]: (bay) => ({ ...bay, isInService: true }),
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
    if (pathOf(request) === `/api/bays/${id}`) {
      state.reads += 1;
      return json(state.stored, 200, { ETag: `"${state.version}"` });
    }
    return json([state.stored]);
  });
  renderAt(<BaysPage />, { role, path: "/bays" });
  return state;
}

describe("BaysPage", () => {
  it("renames a bay against the version it loaded", async () => {
    const api = bayApi();

    const name = await screen.findByLabelText("Name of Lift 1");
    await userEvent.clear(name);
    await userEvent.type(name, "Lift 1 (alignment)");
    await userEvent.click(screen.getByRole("button", { name: "Rename" }));

    expect(await screen.findByLabelText("Name of Lift 1 (alignment)")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Rename" })).not.toBeInTheDocument();
    expect(api.changes).toEqual(['"3"']);
  });

  it("takes a bay out of service and back with the version of each answer", async () => {
    const api = bayApi();

    await userEvent.click(await screen.findByRole("button", { name: "Take out of service" }));
    await userEvent.click(await screen.findByRole("button", { name: "Return to service" }));

    expect(await screen.findByRole("button", { name: "Take out of service" })).toBeInTheDocument();
    expect(api.changes).toEqual(['"3"', '"4"']);
  });

  it("shows the bay as it is now when a colleague changed it in the meantime", async () => {
    const api = bayApi();
    await screen.findByRole("button", { name: "Take out of service" });
    // A colleague takes the lift out of service from another desk.
    api.stored = { ...api.stored, isInService: false };
    api.version += 1;

    await userEvent.click(screen.getByRole("button", { name: "Take out of service" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
    expect(await screen.findByRole("button", { name: "Return to service" })).toBeInTheDocument();
    expect(screen.getByText("Out of service")).toBeInTheDocument();
    expect(api.changes).toEqual(['"3"']);
  });

  it("does not load each bay for staff who cannot change bays", async () => {
    const api = bayApi("Mechanic");

    expect(await screen.findByText("Lift 1")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Take out of service" })).not.toBeInTheDocument();
    expect(api.reads).toBe(0);
  });
});
