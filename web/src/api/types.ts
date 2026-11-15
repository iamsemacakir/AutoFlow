import type { components } from "./schema";

type Schemas = components["schemas"];

export type AppointmentDetails = Schemas["AppointmentDetails"];
export type BayDetails = Schemas["BayDetails"];
export type CheckedInAppointment = Schemas["CheckedInAppointment"];
export type CustomerDetails = Schemas["CustomerDetails"];
export type DaySchedule = Schemas["DaySchedule"];
export type InvoiceDetails = Schemas["InvoiceDetails"];
export type SignedIn = Schemas["SignedIn"];
export type UserDetails = Schemas["UserDetails"];
export type VehicleDetails = Schemas["VehicleDetails"];
export type WorkOrderDetails = Schemas["WorkOrderDetails"];

/** The roles the API knows, as it names them. */
export type Role = "Owner" | "ServiceAdvisor" | "Mechanic";
