import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import type { WorkOrderDetails } from "../api/types";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { WorkOrderPage } from "./WorkOrderPage";

type Line = WorkOrderDetails["lines"][number];

const labour = (id: string, number: number, description: string, hours: number, rate: number): Line => ({
  id,
  number,
  kind: "Labour",
  description,
  partNumber: null,
  quantity: hours,
  unitPrice: rate,
  total: Math.round(hours * rate * 100) / 100,
});

const part = (id: string, number: number, description: string, quantity: number, price: number): Line => ({
  id,
  number,
  kind: "Part",
  description,
  partNumber: "BP-1",
  quantity,
  unitPrice: price,
  total: Math.round(quantity * price * 100) / 100,
});

/** A work order with its totals worked out from `lines`, as the API answers. */
function workOrder(id: string, status: string, lines: Line[], extra: Partial<WorkOrderDetails> = {}): WorkOrderDetails {
  const sum = (kind: string) => lines.filter((line) => line.kind === kind).reduce((total, line) => total + line.total, 0);
  return {
    id,
    vehicleId: "vehicle-1",
    customerId: "customer-1",
    complaint: "Grinding noise when braking",
    mileageKmAtIntake: 48950,
    status,
    lines,
    labourTotal: sum("Labour"),
    partsTotal: sum("Part"),
    total: sum("Labour") + sum("Part"),
    approvedTotal: null,
    createdAt: "2026-11-20T08:00:00Z",
    approvedAt: null,
    startedAt: null,
    completedAt: null,
    cancelledAt: null,
    cancellationReason: null,
    ...extra,
  } as WorkOrderDetails;
}

/**
 * The API for one work order whose version the test can bump, as if a
 * colleague changed it. Every change is checked against `If-Match` (412 when
 * it names an older version) and recorded with the header it carried.
 */
function workOrderApi(initial: WorkOrderDetails) {
  const path = `/api/work-orders/${initial.id}`;
  const state = {
    stored: initial,
    version: 7,
    changes: [] as { path: string; ifMatch: string | null; body: unknown }[],
  };
  const answer = () => json(state.stored, 200, { ETag: `"${state.version}"` });

  mockApi(async (request) => {
    const requestPath = pathOf(request);
    if (requestPath === path && request.method === "GET") {
      return answer();
    }
    if (requestPath.startsWith(path)) {
      const action = requestPath.slice(path.length);
      const text = await request.text();
      const body = text ? (JSON.parse(text) as Record<string, unknown>) : null;
      state.changes.push({ path: action, ifMatch: request.headers.get("If-Match"), body });
      if (request.headers.get("If-Match") !== `"${state.version}"`) {
        return json({ status: 412, code: "request.precondition_failed", title: "Precondition failed" }, 412);
      }
      state.stored = change(state.stored, action, body);
      state.version += 1;
      return answer();
    }
    return json({ status: 404, code: "route.not_found", title: "Not found" }, 404);
  });

  return { state };
}

/** What the API does with each change, just enough for the page to show the result. */
function change(current: WorkOrderDetails, action: string, body: Record<string, unknown> | null): WorkOrderDetails {
  const lines = current.lines;
  const next = lines.length + 1;
  switch (action) {
    case "/labour-lines":
      return rebuild(current, [
        ...lines,
        labour(`line-${next}`, next, String(body?.description), Number(body?.hours), Number(body?.hourlyRate)),
      ]);
    case "/part-lines":
      return rebuild(current, [
        ...lines,
        part(`line-${next}`, next, String(body?.description), Number(body?.quantity), Number(body?.unitPrice)),
      ]);
    case "/approve":
      return { ...current, status: "Approved", approvedTotal: current.total, approvedAt: "2026-11-20T09:00:00Z" };
    case "/start":
      return { ...current, status: "InProgress", startedAt: "2026-11-20T10:00:00Z" };
    case "/complete":
      return { ...current, status: "Completed", completedAt: "2026-11-20T12:00:00Z" };
    case "/cancel":
      return { ...current, status: "Cancelled", cancellationReason: String(body?.reason) };
    default:
      return rebuild(current, lines.filter((line) => `/lines/${line.id}` !== action));
  }
}

const rebuild = (current: WorkOrderDetails, lines: Line[]) =>
  workOrder(current.id, current.status, lines, {
    approvedTotal: current.approvedTotal,
    approvedAt: current.approvedAt,
    startedAt: current.startedAt,
  });

function renderWorkOrder(id: string, role: "Owner" | "ServiceAdvisor" | "Mechanic" = "ServiceAdvisor") {
  return renderAt(
    <Routes>
      <Route path="/work-orders/:id" element={<WorkOrderPage />} />
      <Route path="/invoices/:id" element={<h1>Invoice page</h1>} />
    </Routes>,
    { role, path: `/work-orders/${id}` },
  );
}

const brakes = labour("line-1", 1, "Replace front pads and discs", 1.5, 95);

describe("WorkOrderPage", () => {
  it("adds a part with If-Match and shows the new totals", async () => {
    const id = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d01";
    const { state } = workOrderApi(workOrder(id, "Draft", [brakes]));
    renderWorkOrder(id);

    expect(await screen.findByText("Replace front pads and discs")).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText("Kind"), "part");
    await userEvent.type(screen.getByLabelText("Description"), "Brake pads, front axle");
    await userEvent.type(screen.getByLabelText("Part number"), "BP-1");
    await userEvent.type(screen.getByLabelText("Quantity"), "1");
    await userEvent.type(screen.getByLabelText("Unit price"), "64.9");
    await userEvent.click(screen.getByRole("button", { name: "Add line" }));

    expect(await screen.findByText("Brake pads, front axle")).toBeInTheDocument();
    expect(screen.getByText("€207.40")).toBeInTheDocument();
    expect(screen.getByLabelText("Description")).toHaveValue("");
    expect(state.changes).toEqual([
      {
        path: "/part-lines",
        ifMatch: '"7"',
        body: { partNumber: "BP-1", description: "Brake pads, front axle", quantity: 1, unitPrice: 64.9 },
      },
    ]);
  });

  it("does not add a line to a work order a colleague changed and keeps what was typed", async () => {
    const id = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d02";
    const { state } = workOrderApi(workOrder(id, "Draft", [brakes]));
    renderWorkOrder(id);

    await userEvent.type(await screen.findByLabelText("Description"), "Wheel alignment");
    await userEvent.type(screen.getByLabelText("Hours"), "0.5");
    await userEvent.type(screen.getByLabelText("Hourly rate"), "95");
    // A colleague removes the brake job while this page is open.
    state.stored = rebuild(state.stored, []);
    state.version += 1;

    await userEvent.click(screen.getByRole("button", { name: "Add line" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
    expect(await screen.findByText("No labour or parts yet.")).toBeInTheDocument();
    expect(screen.getByLabelText("Description")).toHaveValue("Wheel alignment");
    expect(state.changes.map((c) => c.ifMatch)).toEqual(['"7"']);

    // Adding again is a deliberate choice, now against the colleague's version.
    await userEvent.click(screen.getByRole("button", { name: "Add line" }));

    expect(await screen.findByText("Wheel alignment")).toBeInTheDocument();
    expect(state.changes.map((c) => c.ifMatch)).toEqual(['"7"', '"8"']);
  });

  it("removes a line with If-Match", async () => {
    const id = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d03";
    const pads = part("line-2", 2, "Brake pads, front axle", 1, 64.9);
    const { state } = workOrderApi(workOrder(id, "Draft", [brakes, pads]));
    renderWorkOrder(id);

    await screen.findByText("Brake pads, front axle");
    await userEvent.click(screen.getAllByRole("button", { name: "Remove" })[1]!);

    await waitFor(() => expect(screen.queryByText("Brake pads, front axle")).not.toBeInTheDocument());
    expect(state.changes).toEqual([{ path: "/lines/line-2", ifMatch: '"7"', body: null }]);
  });

  it("walks the status flow with If-Match: approve, start, complete", async () => {
    const id = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d04";
    const { state } = workOrderApi(workOrder(id, "Draft", [brakes]));
    renderWorkOrder(id);

    await userEvent.click(await screen.findByRole("button", { name: "Customer approved the estimate" }));
    await userEvent.click(await screen.findByRole("button", { name: "Start work" }));
    await userEvent.click(await screen.findByRole("button", { name: "Work is done" }));

    expect(await screen.findByRole("button", { name: "Issue invoice" })).toBeInTheDocument();
    expect(screen.getByText("Completed", { selector: ".badge" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add line" })).not.toBeInTheDocument();
    expect(state.changes.map((c) => [c.path, c.ifMatch])).toEqual([
      ["/approve", '"7"'],
      ["/start", '"8"'],
      ["/complete", '"9"'],
    ]);
  });

  it("shows the newer version when a colleague changed the work order before approval", async () => {
    const id = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d05";
    const { state } = workOrderApi(workOrder(id, "Draft", [brakes]));
    renderWorkOrder(id);

    const approve = await screen.findByRole("button", { name: "Customer approved the estimate" });
    // A colleague adds a part, so the customer has not seen this estimate.
    state.stored = rebuild(state.stored, [brakes, part("line-2", 2, "Brake fluid", 1, 18)]);
    state.version += 1;

    await userEvent.click(approve);

    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
    expect(await screen.findByText("Brake fluid")).toBeInTheDocument();
    expect(screen.getByText("Draft", { selector: ".badge" })).toBeInTheDocument();
    expect(state.changes.map((c) => c.path)).toEqual(["/approve"]);
  });

  it("cancels with the reason given and If-Match", async () => {
    const id = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d06";
    const { state } = workOrderApi(workOrder(id, "Approved", [brakes], { approvedTotal: 142.5 }));
    renderWorkOrder(id, "Owner");

    const cancel = await screen.findByRole("button", { name: "Cancel work order" });
    expect(cancel).toBeDisabled();
    await userEvent.type(screen.getByPlaceholderText("Reason for cancelling"), "Customer sold the car");
    await userEvent.click(cancel);

    expect(await screen.findByText("Customer sold the car")).toBeInTheDocument();
    expect(state.changes).toEqual([{ path: "/cancel", ifMatch: '"7"', body: { reason: "Customer sold the car" } }]);
  });

  it("lets mechanics start work but not approve or cancel", async () => {
    const draft = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d07";
    workOrderApi(workOrder(draft, "Draft", [brakes]));
    const { unmount } = renderWorkOrder(draft, "Mechanic");

    await screen.findByText("Replace front pads and discs");
    expect(screen.queryByRole("button", { name: "Customer approved the estimate" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Cancel work order" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add line" })).toBeInTheDocument();
    unmount();

    const approved = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d08";
    workOrderApi(workOrder(approved, "Approved", [brakes], { approvedTotal: 142.5 }));
    renderWorkOrder(approved, "Mechanic");

    expect(await screen.findByRole("button", { name: "Start work" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Cancel work order" })).not.toBeInTheDocument();
  });

  it("points out a total that differs from the approved estimate", async () => {
    const id = "5c1e7a2b-0d4f-4b6a-8e3c-1f2a3b4c5d09";
    workOrderApi(workOrder(id, "InProgress", [brakes, part("line-2", 2, "Brake fluid", 1, 18)], { approvedTotal: 142.5 }));
    renderWorkOrder(id);

    expect(await screen.findByText(/the total has changed since approval/)).toBeInTheDocument();
    expect(screen.getByText("€160.50")).toBeInTheDocument();
  });
});
