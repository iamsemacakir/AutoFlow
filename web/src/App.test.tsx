import { screen, within } from "@testing-library/react";
import { App } from "./App";
import { json, mockApi } from "./test/http";
import { renderAt } from "./test/render";

const emptySchedule = {
  date: "2026-11-20",
  timeZone: "UTC",
  start: "2026-11-20T00:00:00Z",
  end: "2026-11-21T00:00:00Z",
  bays: [],
};

describe("App", () => {
  beforeEach(() => mockApi(() => json(emptySchedule)));

  it("sends visitors who are not signed in to the sign-in page", () => {
    renderAt(<App />, { path: "/customers" });

    expect(screen.getByRole("button", { name: "Sign in" })).toBeInTheDocument();
  });

  it("shows owners every section", () => {
    renderAt(<App />, { role: "Owner" });

    const nav = within(screen.getByRole("navigation", { name: "Main" }));
    expect(nav.getAllByRole("link").map((link) => link.textContent)).toEqual([
      "Schedule",
      "Customers",
      "Work orders",
      "Invoices",
      "Bays",
      "Staff",
    ]);
  });

  it("hides invoices and staff from mechanics, also when they type the address", () => {
    renderAt(<App />, { role: "Mechanic", path: "/invoices" });

    const nav = within(screen.getByRole("navigation", { name: "Main" }));
    expect(nav.queryByText("Invoices")).not.toBeInTheDocument();
    expect(nav.queryByText("Staff")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Schedule" })).toBeInTheDocument();
  });
});
