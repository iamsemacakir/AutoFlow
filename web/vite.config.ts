/// <reference types="vitest/config" />
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// In development the API runs on :5080 (dotnet run); the dev server forwards
// /api to it so the browser only ever talks to one origin, as in production
// where nginx does the same (see web/nginx.conf).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": { target: process.env.AUTOFLOW_API_URL ?? "http://localhost:5080", changeOrigin: true },
    },
  },
  test: {
    include: ["src/**/*.test.{ts,tsx}"],
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    css: false,
  },
});
