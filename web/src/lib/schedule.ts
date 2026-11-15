import type { AppointmentDetails } from "../api/types";

/** The part of the day the schedule shows, in hours of local wall-clock time. */
export const VISIBLE_HOURS = { from: 7, to: 19 } as const;

export interface Placement {
  /** Offset from the left edge, in percent of the visible window. */
  left: number;
  /** Width in percent of the visible window. */
  width: number;
  /** The appointment starts before or ends after the visible window. */
  clippedStart: boolean;
  clippedEnd: boolean;
}

/**
 * Where an appointment's block goes on the time line of `date` (`YYYY-MM-DD`,
 * read in the browser's time zone like the schedule itself). The window is
 * local wall-clock time, so it stays 07:00–19:00 on days when the clocks
 * change. Appointments entirely outside the window get no block.
 */
export function place(
  appointment: Pick<AppointmentDetails, "start" | "end">,
  date: string,
  hours: { from: number; to: number } = VISIBLE_HOURS,
): Placement | null {
  const windowStart = localTime(date, hours.from);
  const windowEnd = localTime(date, hours.to);
  const start = new Date(appointment.start).getTime();
  const end = new Date(appointment.end).getTime();

  if (end <= windowStart || start >= windowEnd) {
    return null;
  }

  const span = windowEnd - windowStart;
  const from = Math.max(start, windowStart);
  const to = Math.min(end, windowEnd);

  return {
    left: ((from - windowStart) / span) * 100,
    width: ((to - from) / span) * 100,
    clippedStart: start < windowStart,
    clippedEnd: end > windowEnd,
  };
}

/** The hour labels along the top of the schedule. */
export function hourLabels(hours: { from: number; to: number } = VISIBLE_HOURS): string[] {
  return Array.from({ length: hours.to - hours.from }, (_, i) => `${String(hours.from + i).padStart(2, "0")}:00`);
}

function localTime(date: string, hour: number): number {
  return new Date(`${date}T${String(hour).padStart(2, "0")}:00:00`).getTime();
}
