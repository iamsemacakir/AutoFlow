import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { VehiclePage } from "./VehiclePage";

const vehicle = {
  id: "vehicle-1",
  customerId: "customer-1",
  vin: "WVWZZZ1KZAW000001",
  licensePlate: "B-AF 101",
  make: "Volkswagen",
  model: "Golf",
  modelYear: 2019,
  mileageKm: 84000,
  registeredAt: "2026-11-01T08:00:00Z",
};

const customer = (id: string, firstName: string, lastName: string) => ({
  id,
  firstName,
  lastName,
  email: `${firstName.toLowerCase()}@example.com`,
  phoneNumber: null,
  createdAt: "2026-11-01T08:00:00Z",
});

const owner = customer("customer-1", "Mia", "Wagner");
const buyer = customer("customer-2", "Jonas", "Wagner");

function renderVehicle(role: "Owner" | "ServiceAdvisor" | "Mechanic") {
  return renderAt(
    <Routes>
      <Route path="/vehicles/:id" element={<VehiclePage />} />
    </Routes>,
    { role, path: "/vehicles/vehicle-1" },
  );
}

/** Answers like the API for one vehicle; `onTransfer` handles the transfer request. */
function vehicleApi(onTransfer: (body: { customerId: string }) => Response) {
  let current = vehicle;
  return mockApi(async (request) => {
    const path = pathOf(request);
    if (path === "/api/vehicles/vehicle-1/transfer") {
      const body = (await request.json()) as { customerId: string };
      const answer = onTransfer(body);
      if (answer.ok) {
        current = { ...current, customerId: body.customerId };
      }
      return answer;
    }
    if (path === "/api/vehicles/vehicle-1") {
      return json(current);
    }
    if (path.startsWith("/api/customers?")) {
      return json({ items: [owner, buyer], page: 1, pageSize: 10, totalCount: 2, totalPages: 1 });
    }
    if (path === "/api/bays") {
      return json([]);
    }
    return json({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
  });
}

describe("VehiclePage", () => {
  it("transfers the vehicle to a customer found by search, leaving out the current owner", async () => {
    const transfers: unknown[] = [];
    const fetchMock = vehicleApi((body) => {
      transfers.push(body);
      return json({ ...vehicle, customerId: body.customerId });
    });
    renderVehicle("ServiceAdvisor");

    await userEvent.type(await screen.findByLabelText("New owner"), "Wagner");
    await userEvent.click(screen.getByRole("button", { name: "Find" }));

    const transferButton = await screen.findByRole("button", { name: "Transfer to Jonas Wagner" });
    expect(screen.queryByRole("button", { name: "Transfer to Mia Wagner" })).not.toBeInTheDocument();
    const searchRequest = fetchMock.mock.calls
      .map(([request]) => pathOf(request as Request))
      .find((path) => path.startsWith("/api/customers?"));
    expect(searchRequest).toContain("search=Wagner");

    await userEvent.click(transferButton);

    expect(await screen.findByRole("status")).toHaveTextContent("Now owned by Jonas Wagner.");
    expect(transfers).toEqual([{ customerId: "customer-2" }]);
    expect(screen.getByRole("link", { name: "Jonas Wagner" })).toHaveAttribute("href", "/customers/customer-2");
    expect(screen.getByRole("link", { name: "← Owner" })).toHaveAttribute("href", "/customers/customer-2");
  });

  it("shows why the API refused a transfer", async () => {
    vehicleApi(() => json({ status: 404, code: "customer.not_found", title: "Customer not found." }, 404));
    renderVehicle("Owner");

    await userEvent.type(await screen.findByLabelText("New owner"), "Jonas");
    await userEvent.click(screen.getByRole("button", { name: "Find" }));
    await userEvent.click(await screen.findByRole("button", { name: "Transfer to Jonas Wagner" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Customer not found.");
  });

  describe("against the version it loaded", () => {
    const id = "3a9d1c7e-5b2f-4e8a-9c1d-7e6f5a4b3c21";
    const path = `/api/vehicles/${id}`;

    /** A vehicle whose version the test can bump, as if a colleague changed it; records each change's `If-Match`. */
    function versionedVehicleApi() {
      const state = { stored: { ...vehicle, id }, version: 3, changes: [] as (string | null)[] };
      mockApi(async (request) => {
        const requestPath = pathOf(request);
        if (requestPath === `${path}/mileage` || requestPath === `${path}/transfer`) {
          state.changes.push(request.headers.get("If-Match"));
          if (request.headers.get("If-Match") !== `"${state.version}"`) {
            return json({ status: 412, code: "request.precondition_failed", title: "Precondition failed" }, 412);
          }
          state.stored = { ...state.stored, ...((await request.json()) as object) };
          state.version += 1;
          return json(state.stored, 200, { ETag: `"${state.version}"` });
        }
        if (requestPath === path) {
          return json(state.stored, 200, { ETag: `"${state.version}"` });
        }
        if (requestPath.startsWith("/api/customers?")) {
          return json({ items: [owner, buyer], page: 1, pageSize: 10, totalCount: 2, totalPages: 1 });
        }
        if (requestPath === "/api/bays") {
          return json([]);
        }
        return json({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
      });
      renderAt(
        <Routes>
          <Route path="/vehicles/:id" element={<VehiclePage />} />
        </Routes>,
        { role: "ServiceAdvisor", path: `/vehicles/${id}` },
      );
      return state;
    }

    it("records mileage with If-Match and shows a colleague's newer reading instead of overwriting it", async () => {
      const api = versionedVehicleApi();
      await userEvent.type(await screen.findByLabelText("Odometer reading"), "85000");
      // A colleague records a reading while this page is open.
      api.stored = { ...api.stored, mileageKm: 86000 };
      api.version += 1;

      await userEvent.click(screen.getByRole("button", { name: "Record mileage" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
      expect(await screen.findByText("86,000 km")).toBeInTheDocument();
      expect(screen.getByLabelText("Odometer reading")).toHaveValue(85000);
      expect(api.changes).toEqual(['"3"']);

      // Recording again is a deliberate choice, now against the colleague's version.
      await userEvent.click(screen.getByRole("button", { name: "Record mileage" }));

      expect(await screen.findByText("85,000 km")).toBeInTheDocument();
      expect(api.changes).toEqual(['"3"', '"4"']);
    });

    it("does not transfer a vehicle someone else transferred in the meantime and shows its new owner", async () => {
      const api = versionedVehicleApi();
      await userEvent.type(await screen.findByLabelText("New owner"), "Wagner");
      await userEvent.click(screen.getByRole("button", { name: "Find" }));
      const transferButton = await screen.findByRole("button", { name: "Transfer to Jonas Wagner" });
      // A colleague hands the vehicle to someone else first.
      api.stored = { ...api.stored, customerId: "customer-3" };
      api.version += 1;

      await userEvent.click(transferButton);

      expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
      expect(api.changes).toEqual(['"3"']);
      expect(screen.queryByRole("status")).not.toBeInTheDocument();
      await waitFor(() =>
        expect(screen.getByRole("link", { name: "← Owner" })).toHaveAttribute("href", "/customers/customer-3"),
      );
    });
  });

  it("does not offer mechanics to change the owner", async () => {
    vehicleApi(() => json(vehicle));
    renderVehicle("Mechanic");

    expect(await screen.findByRole("heading", { name: /Volkswagen Golf/ })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Change owner" })).not.toBeInTheDocument();
  });

  describe("appointments", () => {
    const appointment = (id: string, start: string, end: string, status: string, extra: object = {}) => ({
      id,
      bayId: "bay-1",
      vehicleId: "vehicle-1",
      customerId: "customer-1",
      start,
      end,
      purpose: `Visit ${id}`,
      status,
      bookedAt: "2026-11-01T08:00:00Z",
      rescheduledAt: null,
      workOrderId: null,
      checkedInAt: null,
      cancelledAt: null,
      cancellationReason: null,
      noShowAt: null,
      ...extra,
    });

    function appointmentsApi(items: unknown[], totalCount = items.length) {
      return mockApi((request) => {
        const path = pathOf(request);
        if (path === "/api/vehicles/vehicle-1") {
          return json(vehicle);
        }
        if (path.startsWith("/api/vehicles/vehicle-1/appointments?")) {
          return json({ items, page: 1, pageSize: 50, totalCount, totalPages: Math.ceil(totalCount / 50) });
        }
        if (path === "/api/bays") {
          return json([{ id: "bay-1", name: "Lift 1", isInService: true, createdAt: "2026-11-01T08:00:00Z" }]);
        }
        return json({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
      });
    }

    it("lists the vehicle's appointments with their bay, linking to the day or the work order", async () => {
      const fetchMock = appointmentsApi([
        appointment("upcoming", "2026-12-01T10:00:00Z", "2026-12-01T11:30:00Z", "Scheduled"),
        appointment("called-off", "2026-11-24T10:00:00Z", "2026-11-24T11:00:00Z", "Cancelled", {
          cancelledAt: "2026-11-20T08:00:00Z",
          cancellationReason: "Customer is ill",
        }),
        appointment("visited", "2026-11-10T10:00:00Z", "2026-11-10T11:00:00Z", "CheckedIn", {
          workOrderId: "work-order-7",
          checkedInAt: "2026-11-10T09:55:00Z",
        }),
      ]);
      renderVehicle("ServiceAdvisor");

      const heading = await screen.findByRole("heading", { name: "Appointments" });
      const card = heading.closest("section") as HTMLElement;
      const rows = await within(card).findAllByRole("row");
      expect(rows).toHaveLength(4);
      const [, upcoming, calledOff, visited] = rows as [HTMLElement, HTMLElement, HTMLElement, HTMLElement];

      expect(within(upcoming).getByText("Visit upcoming")).toBeInTheDocument();
      expect(within(upcoming).getByText("Lift 1")).toBeInTheDocument();
      expect(within(upcoming).getByText("Scheduled")).toBeInTheDocument();
      expect(within(upcoming).getByRole("link", { name: "On the schedule" })).toHaveAttribute("href", "/?date=2026-12-01");
      expect(within(calledOff).getByText("Cancelled")).toBeInTheDocument();
      expect(within(calledOff).queryByRole("link")).not.toBeInTheDocument();
      expect(within(visited).getByText("Checked in")).toBeInTheDocument();
      expect(within(visited).getByRole("link", { name: "Work order" })).toHaveAttribute("href", "/work-orders/work-order-7");
      expect(screen.queryByText(/The latest/)).not.toBeInTheDocument();

      const request = fetchMock.mock.calls
        .map(([sent]) => pathOf(sent as Request))
        .find((path) => path.startsWith("/api/vehicles/vehicle-1/appointments?"));
      expect(request).toContain("pageSize=50");
    });

    it("says how many appointments are left out when there are more than one page", async () => {
      appointmentsApi([appointment("upcoming", "2026-12-01T10:00:00Z", "2026-12-01T11:00:00Z", "Scheduled")], 51);
      renderVehicle("Owner");

      expect(await screen.findByText("The latest 1 of 51 appointments.")).toBeInTheDocument();
    });

    it("shows mechanics the appointments too, or that there are none", async () => {
      appointmentsApi([]);
      renderVehicle("Mechanic");

      expect(await screen.findByText("No appointments yet.")).toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: "Book an appointment" })).not.toBeInTheDocument();
    });
  });
});
