import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import type { InvoiceDetails } from "../api/types";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { InvoicePage } from "./InvoicePage";

const id = "5d8b3e2a-9c4f-4a1b-8e7d-6c5b4a3f2e10";

const issued: InvoiceDetails = {
  id,
  number: "2026-0042",
  workOrderId: "work-order-1",
  customerId: "customer-1",
  customerName: "Mia Wagner",
  vehicleDescription: "Volkswagen Golf (B-AF 101)",
  status: "Issued",
  issuedAt: "2026-11-16T09:00:00Z",
  dueDate: "2026-11-30",
  isOverdue: false,
  vatRatePercent: 19,
  netTotal: 100,
  vatAmount: 19,
  grossTotal: 119,
  amountPaid: 0,
  balance: 119,
  paidAt: null,
  lines: [{ number: 1, kind: "Labour", description: "Annual service", partNumber: null, quantity: 1, unitPrice: 100, total: 100 }],
  payments: [],
};

/** The invoice as the API keeps it, with a version the test can bump; records each payment's `If-Match`. */
function invoiceApi() {
  const state = { stored: issued, version: 8, payments: [] as (string | null)[] };
  mockApi(async (request) => {
    if (pathOf(request) === `/api/invoices/${id}/payments`) {
      state.payments.push(request.headers.get("If-Match"));
      if (request.headers.get("If-Match") !== `"${state.version}"`) {
        return json({ status: 412, code: "request.precondition_failed", title: "Precondition failed" }, 412);
      }
      const { amount, method } = (await request.json()) as { amount: number; method: string };
      state.stored = pay(state.stored, amount, method);
      state.version += 1;
      return json(state.stored, 200, { ETag: `"${state.version}"` });
    }
    return json(state.stored, 200, { ETag: `"${state.version}"` });
  });
  renderAt(
    <Routes>
      <Route path="/invoices/:id" element={<InvoicePage />} />
    </Routes>,
    { role: "ServiceAdvisor", path: `/invoices/${id}` },
  );
  return state;
}

function pay(invoice: InvoiceDetails, amount: number, method: string): InvoiceDetails {
  const amountPaid = invoice.amountPaid + amount;
  return {
    ...invoice,
    amountPaid,
    balance: invoice.grossTotal - amountPaid,
    status: amountPaid >= invoice.grossTotal ? "Paid" : "PartiallyPaid",
    payments: [
      ...invoice.payments,
      { id: `payment-${invoice.payments.length + 1}`, amount, method, receivedAt: "2026-11-17T10:00:00Z" },
    ],
  };
}

describe("InvoicePage", () => {
  it("records a payment against the version it loaded", async () => {
    const api = invoiceApi();

    await userEvent.type(await screen.findByLabelText("Amount received"), "50");
    await userEvent.click(screen.getByRole("button", { name: "Record payment" }));

    expect(await screen.findByText("Partially paid")).toBeInTheDocument();
    expect(api.payments).toEqual(['"8"']);
  });

  it("does not take a payment twice when a colleague recorded one in the meantime", async () => {
    const api = invoiceApi();
    await userEvent.click(await screen.findByRole("button", { name: "Full balance" }));
    // A colleague records the customer's card payment at the other desk.
    api.stored = pay(api.stored, 119, "Card");
    api.version += 1;

    await userEvent.click(screen.getByRole("button", { name: "Record payment" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
    expect(await screen.findByText("Paid", { selector: ".badge" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Record payment" })).not.toBeInTheDocument();
    expect(api.payments).toEqual(['"8"']);
  });
});
