import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { json, mockApi, pathOf } from "../test/http";
import type { AppointmentDetails } from "../api/types";
import { renderAt } from "../test/render";
import { SchedulePage } from "./SchedulePage";

const at = (time: string) => new Date(`2026-11-20T${time}:00`).toISOString();

const schedule = {
  date: "2026-11-20",
  timeZone: "Europe/Berlin",
  start: at("00:00"),
  end: new Date(new Date(at("00:00")).getTime() + 24 * 3600 * 1000).toISOString(),
  bays: [
    {
      bay: { id: "bay-1", name: "Lift 1", isInService: true, createdAt: "2026-11-01T08:00:00Z" },
      appointments: [
        {
          id: "appointment-1",
          bayId: "bay-1",
          vehicleId: "vehicle-1",
          customerId: "customer-1",
          start: at("09:00"),
          end: at("10:30"),
          purpose: "Annual service",
          status: "Scheduled",
          bookedAt: "2026-11-10T08:00:00Z",
          rescheduledAt: null,
          workOrderId: null,
          checkedInAt: null,
          cancelledAt: null,
          cancellationReason: null,
          noShowAt: null,
        },
      ],
    },
    {
      bay: { id: "bay-2", name: "Lift 2", isInService: false, createdAt: "2026-11-01T08:00:00Z" },
      appointments: [],
    },
  ],
};

const listed = schedule.bays[0]!.appointments[0]!;

/** Answers like the API: the opened appointment on its own, everything else through `handler` (the schedule by default). */
function scheduleApi(handler: (request: Request) => Response | Promise<Response> = () => json(schedule)) {
  return mockApi((request) =>
    request.method === "GET" && pathOf(request) === "/api/appointments/appointment-1" ? json(listed) : handler(request),
  );
}

/** A button once it can be used: the panel's actions wait for the appointment's version. */
async function enabledButton(name: string) {
  const button = await screen.findByRole("button", { name });
  await waitFor(() => expect(button).toBeEnabled());
  return button;
}

describe("SchedulePage", () => {
  it("asks for the chosen day in the browser's time zone and draws each bay's appointments", async () => {
    const fetchMock = scheduleApi();
    renderAt(<SchedulePage />, { role: "ServiceAdvisor", path: "/?date=2026-11-20" });

    const block = await screen.findByRole("button", { name: /Annual service/ });
    expect(block).toHaveStyle({ left: "16.666666666666664%", width: "12.5%" });
    expect(screen.getByText("Lift 2")).toBeInTheDocument();
    expect(screen.getByText("out of service")).toBeInTheDocument();

    const request = fetchMock.mock.calls[0]![0] as Request;
    expect(pathOf(request)).toMatch(/^\/api\/schedule\?date=2026-11-20&timeZone=/);
  });

  it("opens an appointment with the front desk's actions", async () => {
    scheduleApi();
    renderAt(<SchedulePage />, { role: "ServiceAdvisor", path: "/?date=2026-11-20" });

    await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));

    expect(await screen.findByRole("button", { name: "Check in" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Cancel appointment" })).toBeDisabled();
  });

  it("shows mechanics the plan without the front desk's actions", async () => {
    scheduleApi();
    renderAt(<SchedulePage />, { role: "Mechanic", path: "/?date=2026-11-20" });

    await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));

    expect(screen.queryByRole("button", { name: "Check in" })).not.toBeInTheDocument();
  });

  it("moves an appointment to another bay and time, starting from where it is", async () => {
    const bays = [
      { id: "bay-1", name: "Lift 1", isInService: true, createdAt: "2026-11-01T08:00:00Z" },
      { id: "bay-2", name: "Lift 2", isInService: false, createdAt: "2026-11-01T08:00:00Z" },
      { id: "bay-3", name: "Lift 3", isInService: true, createdAt: "2026-11-01T08:00:00Z" },
    ];
    const moves: unknown[] = [];
    const fetchMock = scheduleApi(async (request) => {
      const path = pathOf(request);
      if (path === "/api/bays") {
        return json(bays);
      }
      if (path === "/api/appointments/appointment-1/reschedule") {
        const body = (await request.json()) as { bayId: string; start: string; end: string };
        moves.push(body);
        return json({ ...schedule.bays[0]!.appointments[0], ...body, rescheduledAt: "2026-11-16T08:00:00Z" });
      }
      return json(schedule);
    });
    renderAt(<SchedulePage />, { role: "ServiceAdvisor", path: "/?date=2026-11-20" });

    await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));
    await userEvent.click(await enabledButton("Reschedule"));
    const form = within(screen.getByRole("form", { name: "Reschedule appointment" }));

    expect(form.getByLabelText("Day")).toHaveValue("2026-11-20");
    expect(form.getByLabelText(/Start/)).toHaveValue("09:00");
    expect(form.getByLabelText("Hours")).toHaveValue(1.5);
    await screen.findByRole("option", { name: "Lift 3" });
    expect(screen.queryByRole("option", { name: "Lift 2" })).not.toBeInTheDocument();

    await userEvent.selectOptions(form.getByLabelText("Bay"), "bay-3");
    await userEvent.clear(form.getByLabelText(/Start/));
    await userEvent.type(form.getByLabelText(/Start/), "11:00");
    await userEvent.click(form.getByRole("button", { name: "Move appointment" }));

    await screen.findByRole("button", { name: /Annual service/ });
    expect(moves).toEqual([{ bayId: "bay-3", start: at("11:00"), end: at("12:30") }]);
    expect(screen.queryByRole("form", { name: "Reschedule appointment" })).not.toBeInTheDocument();
    const scheduleRequests = fetchMock.mock.calls.filter(([request]) => pathOf(request as Request).startsWith("/api/schedule"));
    expect(scheduleRequests.length).toBeGreaterThan(1);
  });

  it("shows why the API refused a move", async () => {
    scheduleApi((request) => {
      const path = pathOf(request);
      if (path === "/api/bays") {
        return json([schedule.bays[0]!.bay]);
      }
      if (path.endsWith("/reschedule")) {
        return json({ status: 409, code: "appointment.bay_already_booked", title: "The bay is already booked at that time." }, 409);
      }
      return json(schedule);
    });
    renderAt(<SchedulePage />, { role: "Owner", path: "/?date=2026-11-20" });

    await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));
    await userEvent.click(await enabledButton("Reschedule"));
    await screen.findByRole("option", { name: "Lift 1" });
    await userEvent.click(screen.getByRole("button", { name: "Move appointment" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The bay is already booked at that time.");
    expect(screen.getByRole("form", { name: "Reschedule appointment" })).toBeInTheDocument();
  });

  describe("against the version it loaded", () => {
    const id = "7c2e9a41-6d3b-4f8e-a1c5-2b9d8e7f6a03";
    const opened: AppointmentDetails = { ...listed, id };

    /** The appointment as the API keeps it; the test changes it as a colleague would. Records each change's `If-Match`. */
    function versionedApi() {
      const state = { stored: opened as AppointmentDetails, version: 5, changes: [] as (string | null)[], reads: 0 };
      mockApi(async (request) => {
        const path = pathOf(request);
        if (path === `/api/appointments/${id}`) {
          state.reads += 1;
          return json(state.stored, 200, { ETag: `"${state.version}"` });
        }
        if (path.startsWith(`/api/appointments/${id}/`)) {
          state.changes.push(request.headers.get("If-Match"));
          if (request.headers.get("If-Match") !== `"${state.version}"`) {
            return json({ status: 412, code: "request.precondition_failed", title: "Precondition failed" }, 412);
          }
          state.stored = path.endsWith("/no-show")
            ? { ...state.stored, status: "NoShow", noShowAt: "2026-11-20T10:00:00Z" }
            : { ...state.stored, rescheduledAt: "2026-11-17T08:00:00Z" };
          state.version += 1;
          return json(state.stored, 200, { ETag: `"${state.version}"` });
        }
        if (path === "/api/bays") {
          return json(schedule.bays.map(({ bay }) => bay));
        }
        return json({ ...schedule, bays: [{ ...schedule.bays[0]!, appointments: [state.stored] }, schedule.bays[1]!] });
      });
      return state;
    }

    it("loads the opened appointment and sends its version with an action", async () => {
      const api = versionedApi();
      renderAt(<SchedulePage />, { role: "ServiceAdvisor", path: "/?date=2026-11-20" });

      await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));
      await userEvent.click(await enabledButton("No-show"));

      await waitFor(() => expect(screen.queryByRole("button", { name: "No-show" })).not.toBeInTheDocument());
      expect(api.changes).toEqual(['"5"']);
      expect(api.reads).toBeGreaterThan(0);
    });

    it("refuses to act on an appointment a colleague changed in the meantime and shows it as it is now", async () => {
      const api = versionedApi();
      renderAt(<SchedulePage />, { role: "ServiceAdvisor", path: "/?date=2026-11-20" });

      await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));
      const noShow = await enabledButton("No-show");
      // A colleague checks the vehicle in while the panel is open.
      api.stored = { ...api.stored, status: "CheckedIn", checkedInAt: "2026-11-20T08:55:00Z", workOrderId: "work-order-9" };
      api.version += 1;

      await userEvent.click(noShow);

      expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this in the meantime.");
      expect(api.changes).toEqual(['"5"']);
      expect(await screen.findByRole("link", { name: "Open work order" })).toHaveAttribute("href", "/work-orders/work-order-9");
      expect(screen.queryByRole("button", { name: "Check in" })).not.toBeInTheDocument();
    });

    it("moves an appointment with its version", async () => {
      const api = versionedApi();
      renderAt(<SchedulePage />, { role: "Owner", path: "/?date=2026-11-20" });

      await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));
      await userEvent.click(await enabledButton("Reschedule"));
      await screen.findByRole("option", { name: "Lift 1" });
      await userEvent.click(screen.getByRole("button", { name: "Move appointment" }));

      await waitFor(() => expect(api.changes).toEqual(['"5"']));
    });
  });
});
