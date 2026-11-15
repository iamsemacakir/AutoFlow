import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end tests against a running AutoFlow with the demo workshop, by
 * default the Docker Compose stack (`docker compose up --build`) on :8080.
 */
export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:8080",
    trace: "retain-on-failure",
    timezoneId: process.env.E2E_TIME_ZONE ?? "Europe/Berlin",
    locale: "en-GB",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 } } }],
});
