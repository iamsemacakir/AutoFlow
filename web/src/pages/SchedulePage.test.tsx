import { screen } from "@testing-library/react";
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
});
