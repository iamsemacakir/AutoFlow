import { hourLabels, place } from "./schedule";

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
