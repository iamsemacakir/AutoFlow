/** The workshop's currency; the API's amounts are all in it. */
export const CURRENCY = import.meta.env.VITE_CURRENCY ?? "EUR";

const money = new Intl.NumberFormat("en-GB", { style: "currency", currency: CURRENCY });
const number = new Intl.NumberFormat("en-GB", { maximumFractionDigits: 2 });
const dateTime = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "short" });
const date = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium" });
const time = new Intl.DateTimeFormat("en-GB", { timeStyle: "short" });

export const formatMoney = (amount: number) => money.format(amount);
export const formatNumber = (value: number) => number.format(value);
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));
export const formatTime = (iso: string) => time.format(new Date(iso));

/** A calendar date as sent by the API (`2026-11-20`), shown without shifting it through a time zone. */
export const formatDate = (isoDate: string) => date.format(new Date(`${isoDate}T12:00:00`));

/** `InProgress` → `In progress`, `ServiceAdvisor` → `Service advisor`. */
export function humanize(name: string): string {
  const words = name.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** Today in the browser's time zone as `YYYY-MM-DD`. */
export function today(now: Date = new Date()): string {
  return toIsoDate(now);
}

export function toIsoDate(value: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

export function addDays(isoDate: string, days: number): string {
  const value = new Date(`${isoDate}T12:00:00`);
  value.setDate(value.getDate() + days);
  return toIsoDate(value);
}

/** The browser's IANA time zone, which the schedule is read in. */
export const browserTimeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
