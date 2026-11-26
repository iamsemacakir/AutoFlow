import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { InvoiceDetails } from "../api/types";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { InvoicesPage } from "./InvoicesPage";

function invoice(overrides: Partial<InvoiceDetails>): InvoiceDetails {
  return {
    id: "7a1c2e3f-0000-4000-8000-000000000001",
    number: "2026-00001",
    workOrderId: "9b2f3c1d-0000-4000-8000-000000000001",
    customerId: "1d4b8e3f-0000-4000-8000-000000000001",
    customerName: "Anna Schmidt",
    vehicleDescription: "Volkswagen Golf (2019), M-AB 123",
    status: "Issued",
    issuedAt: "2026-11-02T09:00:00Z",
    dueDate: "2026-11-16",
    isOverdue: false,
    vatRatePercent: 19,
    netTotal: 100,
    vatAmount: 19,
    grossTotal: 119,
    amountPaid: 0,
    balance: 119,
    paidAt: null,
    lines: [],
    payments: [],
    ...overrides,
  };
}

const overdueOpen = invoice({ number: "2026-00003", isOverdue: true });
const overduePart = invoice({
  id: "7a1c2e3f-0000-4000-8000-000000000002",
  number: "2026-00002",
  status: "PartiallyPaid",
  isOverdue: true,
  amountPaid: 50,
  balance: 69,
});
const paid = invoice({
  id: "7a1c2e3f-0000-4000-8000-000000000003",
  number: "2026-00001",
  status: "Paid",
  amountPaid: 119,
  balance: 0,
  paidAt: "2026-11-05T10:00:00Z",
});

/** Answers the list like the API does: by `status` and `overdue`, two per page; records every query it was sent. */
function invoicesApi(all: InvoiceDetails[]) {
  const queries: string[] = [];
  mockApi((request) => {
    const url = new URL(request.url);
    queries.push(pathOf(request));
    const status = url.searchParams.get("status");
    const overdue = url.searchParams.get("overdue");
    const page = Number(url.searchParams.get("page") ?? "1");
    const matching = all.filter(
      (i) => (!status || i.status === status) && (overdue === null || String(i.isOverdue) === overdue),
    );
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

describe("InvoicesPage", () => {
  it("lists every invoice and marks the overdue ones", async () => {
    const queries = invoicesApi([overduePart, paid]);

    renderAt(<InvoicesPage />, { role: "ServiceAdvisor", path: "/invoices" });

    const row = (await screen.findByRole("link", { name: "2026-00002" })).closest("tr")!;
    expect(within(row).getByText(/overdue/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "2026-00002" })).toHaveAttribute("href", `/invoices/${overduePart.id}`);
    const paidRow = screen.getByRole("link", { name: "2026-00001" }).closest("tr")!;
    expect(within(paidRow).queryByText(/overdue/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Only overdue" })).toHaveAttribute("aria-pressed", "false");
    expect(queries[0]).toBe("/api/invoices?page=1&pageSize=20");
  });

  it("shows only the overdue invoices and keeps the filter in the address with the status", async () => {
    const queries = invoicesApi([overdueOpen, overduePart, paid]);
    renderAt(<InvoicesPage />, { role: "Owner", path: "/invoices" });
    await screen.findByRole("link", { name: "2026-00003" });

    await userEvent.click(screen.getByRole("button", { name: "Only overdue" }));

    expect(await screen.findByRole("button", { name: "Only overdue" })).toHaveAttribute("aria-pressed", "true");
    expect(queries).toContain("/api/invoices?overdue=true&page=1&pageSize=20");

    await userEvent.click(screen.getByRole("button", { name: "Partially paid" }));

    expect(await screen.findByRole("button", { name: "Partially paid" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Only overdue" })).toHaveAttribute("aria-pressed", "true");
    expect(queries).toContain("/api/invoices?status=PartiallyPaid&overdue=true&page=1&pageSize=20");
  });

  it("starts from the address, says when nothing is overdue and pages with the filters", async () => {
    const many = [4, 5, 6].map((n) =>
      invoice({ id: `7a1c2e3f-0000-4000-8000-00000000001${n}`, number: `2026-0000${n}`, isOverdue: true }),
    );
    const queries = invoicesApi([...many, paid]);
    renderAt(<InvoicesPage />, { role: "Owner", path: "/invoices?status=Issued&overdue=true" });

    expect(await screen.findByText("Page 1 of 2")).toBeInTheDocument();
    expect(queries[0]).toBe("/api/invoices?status=Issued&overdue=true&page=1&pageSize=20");
    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(await screen.findByRole("link", { name: "2026-00006" })).toBeInTheDocument();
    expect(queries).toContain("/api/invoices?status=Issued&overdue=true&page=2&pageSize=20");

    await userEvent.click(screen.getByRole("button", { name: "Paid" }));

    expect(await screen.findByText("No overdue invoices.")).toBeInTheDocument();
    expect(queries).toContain("/api/invoices?status=Paid&overdue=true&page=1&pageSize=20");
  });
});
