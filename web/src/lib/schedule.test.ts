import { hourLabels, place, slotFieldsOf, slotFrom } from "./schedule";

const local = (time: string) => new Date(`2026-11-20T${time}:00`).toISOString();

describe("place", () => {
  it("puts an appointment inside the window at its share of the twelve hours", () => {
    const placement = place({ start: local("10:00"), end: local("13:00") }, "2026-11-20");

    expect(placement).toEqual({ left: 25, width: 25, clippedStart: false, clippedEnd: false });
  });

  it("clips appointments that start before or end after the window", () => {
    const early = place({ start: local("06:00"), end: local("08:00") }, "2026-11-20");
    const late = place({ start: local("18:00"), end: local("21:00") }, "2026-11-20");

    expect(early).toMatchObject({ left: 0, clippedStart: true, clippedEnd: false });
    expect(early!.width).toBeCloseTo(100 / 12);
    expect(late).toMatchObject({ clippedStart: false, clippedEnd: true });
    expect(late!.left + late!.width).toBeCloseTo(100);
  });

  it("leaves out appointments outside the window", () => {
    expect(place({ start: local("05:00"), end: local("07:00") }, "2026-11-20")).toBeNull();
    expect(place({ start: local("19:00"), end: local("20:00") }, "2026-11-20")).toBeNull();
  });
});

describe("hourLabels", () => {
  it("labels every hour from 07:00 to 18:00", () => {
    const labels = hourLabels();

    expect(labels).toHaveLength(12);
    expect(labels[0]).toBe("07:00");
    expect(labels.at(-1)).toBe("18:00");
  });
});

describe("slotFrom", () => {
  it("turns a local day, start time and length into UTC instants", () => {
    const slot = slotFrom({ date: "2026-11-20", time: "09:15", hours: "1.5" });

    expect(slot).toEqual({ start: local("09:15"), end: local("10:45") });
  });

  it("measures the length in elapsed time when the clocks change", () => {
    const slot = slotFrom({ date: "2026-03-29", time: "01:00", hours: "3" });

    expect(new Date(slot.end).getTime() - new Date(slot.start).getTime()).toBe(3 * 60 * 60 * 1000);
  });
});

describe("slotFieldsOf", () => {
  it("shows an appointment's slot as the form fields it was booked with", () => {
    const fields = slotFieldsOf({ start: local("14:30"), end: local("16:00") });

    expect(fields).toEqual({ date: "2026-11-20", time: "14:30", hours: "1.5" });
    expect(slotFrom(fields)).toEqual({ start: local("14:30"), end: local("16:00") });
  });
});
