import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { json, mockApi, pathOf } from "../test/http";
import { renderAt } from "../test/render";
import { LoginPage } from "./LoginPage";

function renderLogin() {
  return renderAt(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<p>Start page</p>} />
    </Routes>,
    { path: "/login" },
  );
}

describe("LoginPage", () => {
  it("signs in and continues to the start page", async () => {
    const fetchMock = mockApi(async (request) => {
      expect(pathOf(request)).toBe("/api/auth/token");
      expect(await request.json()).toEqual({ email: "owner@example.com", password: "correct horse battery" });
      return json({
        accessToken: "a.b.c",
        tokenType: "Bearer",
        expiresAt: "2999-01-01T00:00:00Z",
        user: {
          id: "1",
          email: "owner@example.com",
          displayName: "Olga Owner",
          role: "Owner",
          isActive: true,
          createdAt: "2026-11-01T08:00:00Z",
          lastSignInAt: null,
        },
      });
    });
    renderLogin();

    await userEvent.type(screen.getByLabelText("E-mail"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Start page")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem("autoflow.session")).toContain("a.b.c");
  });

  it("shows the API's message when the credentials are wrong", async () => {
    mockApi(() =>
      json(
        { status: 401, title: "Unauthorized", detail: "The e-mail address or the password is wrong.", code: "auth.invalid_credentials" },
        401,
      ),
    );
    renderLogin();

    await userEvent.type(screen.getByLabelText("E-mail"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "not the password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The e-mail address or the password is wrong.");
    expect(localStorage.getItem("autoflow.session")).toBeNull();
  });
});
