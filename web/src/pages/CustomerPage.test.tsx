import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { CustomerPage } from "./CustomerPage";

const customer = {
  id: "customer-1",
  firstName: "Mia",
  lastName: "Wagner",
  email: "mia@example.com",
  phoneNumber: null,
  createdAt: "2026-11-01T08:00:00Z",
};

function renderCustomer(role: "Owner" | "ServiceAdvisor" | "Mechanic") {
  return renderAt(
    <Routes>
      <Route path="/customers/:id" element={<CustomerPage />} />
      <Route path="/customers" element={<h1>All customers</h1>} />
    </Routes>,
    { role, path: "/customers/customer-1" },
  );
}

/** An empty page of a list, as the API answers when nothing matches. */
const emptyPage = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };

/** Answers like the API for one customer without vehicles; `onDelete` answers the delete request. */
function customerApi(onDelete: () => Response) {
  return mockApi((request) => {
    const path = pathOf(request);
    if (request.method === "DELETE" && path === "/api/customers/customer-1") {
      return onDelete();
    }
    if (path === "/api/customers/customer-1/vehicles") {
      return json([]);
    }
    if (path.startsWith("/api/work-orders?") || path.startsWith("/api/invoices?")) {
      return json(emptyPage);
    }
    return json(customer);
  });
}

describe("CustomerPage", () => {
  it("deletes a customer after a second click and goes back to the list", async () => {
    const fetchMock = customerApi(() => new Response(null, { status: 204 }));
    renderCustomer("ServiceAdvisor");

    await userEvent.click(await screen.findByRole("button", { name: "Delete customer" }));
    expect(fetchMock.mock.calls.some(([request]) => (request as Request).method === "DELETE")).toBe(false);

    await userEvent.click(screen.getByRole("button", { name: "Delete Mia Wagner for good" }));

    expect(await screen.findByRole("heading", { name: "All customers" })).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([request]) => (request as Request).method === "DELETE")).toHaveLength(1);
  });

  it("keeps the customer when the second click is not given", async () => {
    const fetchMock = customerApi(() => new Response(null, { status: 204 }));
    renderCustomer("Owner");

    await userEvent.click(await screen.findByRole("button", { name: "Delete customer" }));
    await userEvent.click(screen.getByRole("button", { name: "Keep" }));

    expect(screen.getByRole("button", { name: "Delete customer" })).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([request]) => (request as Request).method === "DELETE")).toBe(false);
  });

  it("stays on the customer and shows the API's reason when they still own vehicles", async () => {
    customerApi(() =>
      json(
        {
          status: 409,
          code: "customer.has_vehicles",
          title: "Conflict",
          detail: "Customer 'customer-1' still owns vehicles. Transfer them to another customer first.",
        },
        409,
      ),
    );
    renderCustomer("Owner");

    await userEvent.click(await screen.findByRole("button", { name: "Delete customer" }));
    await userEvent.click(screen.getByRole("button", { name: "Delete Mia Wagner for good" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Transfer them to another customer first.");
    expect(screen.getByRole("heading", { name: "Mia Wagner" })).toBeInTheDocument();
  });

  it("does not offer mechanics to delete customers", async () => {
    customerApi(() => new Response(null, { status: 204 }));
    renderCustomer("Mechanic");

    expect(await screen.findByRole("heading", { name: "Mia Wagner" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete customer" })).not.toBeInTheDocument();
  });

  it("saves changes against the version it loaded and shows a colleague's newer version instead of overwriting it", async () => {
    const id = "6f1c2b9e-0d4a-4c1e-9a53-1b2c3d4e5f60";
    let stored = { ...customer, id, phoneNumber: "+49 30 1111" };
    let version = 7;
    const saves: (string | null)[] = [];
    mockApi(async (request) => {
      const path = pathOf(request);
      if (path === `/api/customers/${id}/vehicles`) {
        return json([]);
      }
      if (path.startsWith("/api/work-orders?") || path.startsWith("/api/invoices?")) {
        return json(emptyPage);
      }
      if (request.method === "PUT") {
        saves.push(request.headers.get("If-Match"));
        if (request.headers.get("If-Match") !== `"${version}"`) {
          return json({ status: 412, code: "request.precondition_failed", title: "Precondition failed" }, 412);
        }
        stored = { ...stored, ...((await request.json()) as object) };
        version += 1;
        return json(stored, 200, { ETag: `"${version}"` });
      }
      return json(stored, 200, { ETag: `"${version}"` });
    });
    renderAt(
      <Routes>
        <Route path="/customers/:id" element={<CustomerPage />} />
      </Routes>,
      { role: "ServiceAdvisor", path: `/customers/${id}` },
    );

    await userEvent.click(await screen.findByRole("button", { name: "Edit details" }));
    // A colleague changes the phone number while the form is open.
    stored = { ...stored, phoneNumber: "+49 30 2222" };
    version += 1;
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
    expect(saves).toEqual(['"7"']);

    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(await screen.findByText("+49 30 2222")).toBeInTheDocument();
  });

  describe("work orders", () => {
    const golf = {
      id: "vehicle-1",
      customerId: "customer-1",
      vin: "WVWZZZ1KZAW000001",
      licensePlate: "B-MW 1",
      make: "VW",
      model: "Golf",
      modelYear: 2019,
      mileageKm: 81000,
      createdAt: "2026-11-01T08:00:00Z",
    };
    const workOrder = (id: string, vehicleId: string, status: string, total: number) => ({
      id,
      vehicleId,
      customerId: "customer-1",
      complaint: `Complaint ${id}`,
      mileageKmAtIntake: 80000,
      status,
      lines: [],
      labourTotal: total,
      partsTotal: 0,
      total,
      approvedTotal: null,
      createdAt: "2026-11-20T08:00:00Z",
      approvedAt: null,
      startedAt: null,
      completedAt: null,
      invoicedAt: null,
      cancelledAt: null,
    });

    function workOrdersApi(items: unknown[], totalCount = items.length) {
      return mockApi((request) => {
        const path = pathOf(request);
        if (path === "/api/customers/customer-1/vehicles") {
          return json([golf]);
        }
        if (path.startsWith("/api/work-orders?")) {
          return json({ items, page: 1, pageSize: 50, totalCount, totalPages: Math.ceil(totalCount / 50) });
        }
        if (path.startsWith("/api/invoices?")) {
          return json(emptyPage);
        }
        return json(customer);
      });
    }

    it("lists the customer's work orders with their vehicle, also for cars sold since", async () => {
      const fetchMock = workOrdersApi([
        workOrder("brakes", "vehicle-1", "InProgress", 240),
        workOrder("clutch", "vehicle-sold", "Invoiced", 980.5),
      ]);
      renderCustomer("Mechanic");

      const heading = await screen.findByRole("heading", { name: "Work orders" });
      const card = heading.closest("section") as HTMLElement;
      const rows = await within(card).findAllByRole("row");
      expect(rows).toHaveLength(3);
      const [, brakes, clutch] = rows as [HTMLElement, HTMLElement, HTMLElement];

      expect(within(brakes).getByRole("link", { name: "Complaint brakes" })).toHaveAttribute("href", "/work-orders/brakes");
      expect(within(brakes).getByRole("link", { name: "VW Golf (B-MW 1)" })).toHaveAttribute("href", "/vehicles/vehicle-1");
      expect(within(brakes).getByText("In progress")).toBeInTheDocument();
      expect(within(clutch).getByRole("link", { name: "Earlier vehicle" })).toHaveAttribute("href", "/vehicles/vehicle-sold");
      expect(within(clutch).getByText("Invoiced")).toBeInTheDocument();
      expect(screen.queryByText(/The latest/)).not.toBeInTheDocument();

      const request = fetchMock.mock.calls
        .map(([sent]) => pathOf(sent as Request))
        .find((path) => path.startsWith("/api/work-orders?"));
      expect(request).toContain("customerId=customer-1");
      expect(request).toContain("pageSize=50");
    });

    it("says how many work orders are left out when there are more than one page", async () => {
      workOrdersApi([workOrder("brakes", "vehicle-1", "Draft", 0)], 51);
      renderCustomer("ServiceAdvisor");

      expect(await screen.findByText("The latest 1 of 51 work orders.")).toBeInTheDocument();
    });

    it("says when the customer has no work orders yet", async () => {
      workOrdersApi([]);
      renderCustomer("Owner");

      expect(await screen.findByText("No work orders yet.")).toBeInTheDocument();
    });
  });
});
