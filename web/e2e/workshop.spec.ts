import { expect, test, type Page } from "@playwright/test";

const password = process.env.E2E_PASSWORD ?? "autoflow-demo";

async function signIn(page: Page, email: string) {
  await page.goto("/login");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Schedule" })).toBeVisible();
}

/** Saves a screenshot for the README when E2E_SCREENSHOTS is set. */
async function capture(page: Page, name: string) {
  if (process.env.E2E_SCREENSHOTS) {
    await page.screenshot({ path: `e2e/screenshots/${name}.png` });
  }
}

test("the front desk plans the day and follows a car from check-in to invoice", async ({ page }) => {
  await signIn(page, "owner@autoflow.demo");

  // The demo day: the first car is checked in, one customer did not come.
  const schedule = page.getByRole("table", { name: /Schedule for/ });
  await expect(schedule.getByText("Lift 1", { exact: true })).toBeVisible();
  await expect(schedule.getByRole("button", { name: /Annual service with oil change/ })).toBeVisible();
  await expect(schedule.getByRole("button", { name: /Seasonal tyre change/ })).toBeVisible();
  await capture(page, "schedule");

  // The checked-in car's work order is in progress.
  await schedule.getByRole("button", { name: /Annual service with oil change/ }).click();
  await page.getByRole("link", { name: "Open work order" }).click();
  await expect(page.getByText("In progress", { exact: true })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Oil filter" })).toBeVisible();
  await capture(page, "work-order");

  // Customers can be found by name.
  await page.getByRole("link", { name: "Customers" }).click();
  await page.getByLabel("Search customers").fill("Wagner");
  await page.getByRole("button", { name: "Search" }).click();
  await page.getByRole("link", { name: "Wagner, Mia" }).click();
  await expect(page.getByRole("heading", { name: "Mia Wagner" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Opel Corsa (2018)" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Exhaust rattles at idle" })).toBeVisible();
  await capture(page, "customer");

  // The overdue invoice is paid at the counter, and its PDF downloads.
  await page.getByRole("link", { name: "Invoices" }).click();
  await expect(page.getByRole("row", { name: /overdue/ })).toBeVisible();
  await capture(page, "invoices");
  await page.getByRole("row", { name: /overdue/ }).getByRole("link").click();
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Download PDF" }).click();
  expect((await download).suggestedFilename()).toMatch(/^invoice-\d{4}-\d{5}\.pdf$/);
  await page.getByRole("button", { name: "Full balance" }).click();
  await page.getByRole("button", { name: "Record payment" }).click();
  await expect(page.locator(".page-header .badge")).toHaveText("Paid");
  await capture(page, "invoice");
});

test("a mechanic sees the plan but not the invoices", async ({ page }) => {
  await signIn(page, "mechanic@autoflow.demo");

  await expect(page.getByRole("link", { name: "Customers" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Invoices" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Staff" })).toHaveCount(0);
});
