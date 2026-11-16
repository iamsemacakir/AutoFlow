import { screen } from "@testing-library/react";
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
});
