import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { WorkOrderDetails } from "../api/types";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { WorkOrdersPage } from "./WorkOrdersPage";

function workOrder(overrides: Partial<WorkOrderDetails>): WorkOrderDetails {
  return {
    id: "9b2f3c1d-0000-4000-8000-000000000001",
    vehicleId: "5c7e1a2b-0000-4000-8000-000000000001",
    customerId: "1d4b8e3f-0000-4000-8000-000000000001",
    complaint: "Annual service",
    mileageKmAtIntake: 62_500,
    status: "Draft",
    lines: [],
    labourTotal: 0,
    partsTotal: 0,
    total: 0,
    approvedTotal: null,
    createdAt: "2026-11-20T08:15:00Z",
    approvedAt: null,
    startedAt: null,
    completedAt: null,
    invoicedAt: null,
    cancelledAt: null,
    cancellationReason: null,
    ...overrides,
  };
}

const brakes = workOrder({
  id: "9b2f3c1d-0000-4000-8000-000000000002",
  vehicleId: "5c7e1a2b-0000-4000-8000-000000000002",
  complaint: "Squeaking brakes",
  status: "InProgress",
  total: 239.5,
});
const service = workOrder({ complaint: "Annual service", status: "Completed", total: 142.5 });

/** Answers the list like the API does: filtered by `status`, two per page; records every query it was sent. */
function workOrdersApi(all: WorkOrderDetails[]) {
  const queries: string[] = [];
  mockApi((request) => {
    const url = new URL(request.url);
    queries.push(pathOf(request));
    const status = url.searchParams.get("status");
    const page = Number(url.searchParams.get("page") ?? "1");
    const matching = all.filter((w) => !status || w.status === status);
    const size = 2;
    return json({
      items: matching.slice((page - 1) * size, page * size),
      page,
      pageSize: size,
      totalCount: matching.length,
      totalPages: Math.max(1, Math.ceil(matching.length / size)),
    });
  });
  return queries;
}

describe("WorkOrdersPage", () => {
  it("lists the workshop's work orders with links to each work order and its vehicle", async () => {
    const queries = workOrdersApi([brakes, service]);

    renderAt(<WorkOrdersPage />, { role: "Mechanic", path: "/work-orders" });

    const row = (await screen.findByRole("link", { name: "Squeaking brakes" })).closest("tr")!;
    expect(within(row).getByText("In progress")).toBeInTheDocument();
    expect(within(row).getByRole("link", { name: "Open vehicle" })).toHaveAttribute(
      "href",
      `/vehicles/${brakes.vehicleId}`,
    );
    expect(screen.getByRole("link", { name: "Squeaking brakes" })).toHaveAttribute("href", `/work-orders/${brakes.id}`);
    expect(screen.getByRole("link", { name: "Annual service" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All" })).toHaveAttribute("aria-pressed", "true");
    expect(queries[0]).toBe("/api/work-orders?page=1&pageSize=20");
  });

  it("filters by status and remembers the filter in the address", async () => {
    const queries = workOrdersApi([brakes, service]);
    renderAt(<WorkOrdersPage />, { role: "ServiceAdvisor", path: "/work-orders" });
    await screen.findByRole("link", { name: "Squeaking brakes" });

    await userEvent.click(screen.getByRole("button", { name: "Completed" }));

    expect(await screen.findByRole("button", { name: "Completed" })).toHaveAttribute("aria-pressed", "true");
    expect(await screen.findByText("Completed", { selector: ".badge" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Squeaking brakes" })).not.toBeInTheDocument();
    expect(queries).toContain("/api/work-orders?status=Completed&page=1&pageSize=20");
  });

  it("starts from the status in the address and says when nothing matches", async () => {
    const queries = workOrdersApi([brakes]);

    renderAt(<WorkOrdersPage />, { role: "Owner", path: "/work-orders?status=Approved" });

    expect(await screen.findByText("No work orders are approved.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approved" })).toHaveAttribute("aria-pressed", "true");
    expect(queries[0]).toBe("/api/work-orders?status=Approved&page=1&pageSize=20");
  });

  it("pages through the list and keeps the status filter", async () => {
    const drafts = [1, 2, 3].map((n) =>
      workOrder({ id: `9b2f3c1d-0000-4000-8000-00000000001${n}`, complaint: `Draft ${n}`, status: "Draft" }),
    );
    const queries = workOrdersApi([...drafts, service]);
    renderAt(<WorkOrdersPage />, { role: "Owner", path: "/work-orders?status=Draft" });

    expect(await screen.findByText("Page 1 of 2")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(await screen.findByRole("link", { name: "Draft 3" })).toBeInTheDocument();
    expect(screen.getByText("Page 2 of 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
    expect(queries).toContain("/api/work-orders?status=Draft&page=2&pageSize=20");
  });
});
