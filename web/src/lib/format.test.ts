import { addDays, formatMoney, humanize, today } from "./format";

describe("format", () => {
  it("turns enum names into words", () => {
    expect(humanize("InProgress")).toBe("In progress");
    expect(humanize("ServiceAdvisor")).toBe("Service advisor");
    expect(humanize("Paid")).toBe("Paid");
  });

  it("moves calendar dates across month and year ends", () => {
    expect(addDays("2026-11-30", 1)).toBe("2026-12-01");
    expect(addDays("2027-01-01", -1)).toBe("2026-12-31");
  });

  it("formats today's date in local time", () => {
    expect(today(new Date(2026, 10, 5, 23, 59))).toBe("2026-11-05");
  });

  it("formats amounts in the workshop's currency with cents", () => {
    expect(formatMoney(1234.5)).toMatch(/1,234\.50/);
  });
});
