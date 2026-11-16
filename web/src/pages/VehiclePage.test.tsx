import { screen } from "@testing-library/react";
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

  it("does not offer mechanics to change the owner", async () => {
    vehicleApi(() => json(vehicle));
    renderVehicle("Mechanic");

    expect(await screen.findByRole("heading", { name: /Volkswagen Golf/ })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Change owner" })).not.toBeInTheDocument();
  });
});
