import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { json, mockApi, pathOf } from "../test/http";
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

describe("SchedulePage", () => {
  it("asks for the chosen day in the browser's time zone and draws each bay's appointments", async () => {
    const fetchMock = mockApi(() => json(schedule));
    renderAt(<SchedulePage />, { role: "ServiceAdvisor", path: "/?date=2026-11-20" });

    const block = await screen.findByRole("button", { name: /Annual service/ });
    expect(block).toHaveStyle({ left: "16.666666666666664%", width: "12.5%" });
    expect(screen.getByText("Lift 2")).toBeInTheDocument();
    expect(screen.getByText("out of service")).toBeInTheDocument();

    const request = fetchMock.mock.calls[0]![0] as Request;
    expect(pathOf(request)).toMatch(/^\/api\/schedule\?date=2026-11-20&timeZone=/);
  });

  it("opens an appointment with the front desk's actions", async () => {
    mockApi(() => json(schedule));
    renderAt(<SchedulePage />, { role: "ServiceAdvisor", path: "/?date=2026-11-20" });

    await userEvent.click(await screen.findByRole("button", { name: /Annual service/ }));

    expect(screen.getByRole("button", { name: "Check in" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel appointment" })).toBeDisabled();
  });

  it("shows mechanics the plan without the front desk's actions", async () => {
    mockApi(() => json(schedule));
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
    const fetchMock = mockApi(async (request) => {
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
    await userEvent.click(screen.getByRole("button", { name: "Reschedule" }));
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
    mockApi((request) => {
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
    await userEvent.click(screen.getByRole("button", { name: "Reschedule" }));
    await screen.findByRole("option", { name: "Lift 1" });
    await userEvent.click(screen.getByRole("button", { name: "Move appointment" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The bay is already booked at that time.");
    expect(screen.getByRole("form", { name: "Reschedule appointment" })).toBeInTheDocument();
  });
});
