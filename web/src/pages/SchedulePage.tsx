import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, unwrap } from "../api/client";
import type { AppointmentDetails } from "../api/types";
import { FRONT_DESK, useAuth } from "../auth/AuthContext";
import { Card, Empty, ErrorMessage, Field, Loading, PageHeader, StatusBadge } from "../components/ui";
import { addDays, browserTimeZone, formatDate, formatTime, today } from "../lib/format";
import { hourLabels, place, slotFieldsOf, slotFrom } from "../lib/schedule";

/** The front desk's day: one row per bay, appointments as blocks on a 07:00–19:00 time line. */
export function SchedulePage() {
  const [params, setParams] = useSearchParams();
  const date = params.get("date") ?? today();
  const timeZone = browserTimeZone();
  const [selected, setSelected] = useState<AppointmentDetails | null>(null);

  const schedule = useQuery({
    queryKey: ["schedule", date, timeZone],
    queryFn: async () =>
      unwrap(await api.GET("/api/schedule", { params: { query: { date, timeZone } } })),
  });

  const goTo = (value: string) => {
    setSelected(null);
    setParams({ date: value });
  };

  return (
    <>
      <PageHeader
        title="Schedule"
        subtitle={`${formatDate(date)} · ${timeZone}`}
        actions={
          <div className="button-group">
            <button type="button" onClick={() => goTo(addDays(date, -1))} aria-label="Previous day">
              ←
            </button>
            <button type="button" onClick={() => goTo(today())}>
              Today
            </button>
            <button type="button" onClick={() => goTo(addDays(date, 1))} aria-label="Next day">
              →
            </button>
            <input type="date" value={date} onChange={(e) => e.target.value && goTo(e.target.value)} aria-label="Date" />
          </div>
        }
      />
      <ErrorMessage error={schedule.error} />
      {schedule.isPending && <Loading />}
      {schedule.data && (
        <Card>
          {schedule.data.bays.length === 0 ? (
            <Empty>
              No bays yet. <Link to="/bays">Add the workshop's bays</Link> to start booking.
            </Empty>
          ) : (
            <div className="timeline" role="table" aria-label={`Schedule for ${date}`}>
              <div className="timeline-row timeline-hours" role="row">
                <div className="timeline-bay" role="columnheader">
                  Bay
                </div>
                <div className="timeline-track" role="columnheader">
                  {hourLabels().map((label) => (
                    <span key={label}>{label}</span>
                  ))}
                </div>
              </div>
              {schedule.data.bays.map(({ bay, appointments }) => (
                <div key={bay.id} className={`timeline-row${bay.isInService ? "" : " out-of-service"}`} role="row">
                  <div className="timeline-bay" role="rowheader">
                    {bay.name}
                    {!bay.isInService && <small className="muted"> out of service</small>}
                  </div>
                  <div className="timeline-track" role="cell">
                    {appointments.map((appointment) => {
                      const placement = place(appointment, date);
                      return (
                        placement && (
                          <button
                            type="button"
                            key={appointment.id}
                            className={`block block-${appointment.status.toLowerCase()}`}
                            style={{ left: `${placement.left}%`, width: `${placement.width}%` }}
                            title={`${formatTime(appointment.start)}–${formatTime(appointment.end)} ${appointment.purpose}`}
                            onClick={() => setSelected(appointment)}
                          >
                            <strong>{formatTime(appointment.start)}</strong> {appointment.purpose}
                          </button>
                        )
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>
      )}
      {selected && (
        <AppointmentPanel
          key={selected.id}
          appointment={selected}
          onClose={() => setSelected(null)}
          onMoved={(moved) => goTo(slotFieldsOf(moved).date)}
        />
      )}
    </>
  );
}

function AppointmentPanel({
  appointment,
  onClose,
  onMoved,
}: {
  appointment: AppointmentDetails;
  onClose: () => void;
  /** Called with the appointment at its new place, after the schedule was refreshed. */
  onMoved: (moved: AppointmentDetails) => void;
}) {
  const { hasRole } = useAuth();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [reason, setReason] = useState("");
  const [moving, setMoving] = useState(false);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["schedule"] });

  const checkIn = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST("/api/appointments/{id}/check-in", { params: { path: { id: appointment.id } }, body: { mileageKm: null, complaint: null } })),
    onSuccess: async (result) => {
      await refresh();
      navigate(`/work-orders/${result.workOrder.id}`);
    },
  });
  const cancel = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST("/api/appointments/{id}/cancel", { params: { path: { id: appointment.id } }, body: { reason } }),
      ),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
  });
  const noShow = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST("/api/appointments/{id}/no-show", { params: { path: { id: appointment.id } } })),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
  });

  const canAct = appointment.status === "Scheduled" && hasRole(...FRONT_DESK);

  return (
    <Card
      title={appointment.purpose}
      actions={
        <button type="button" className="link" onClick={onClose}>
          Close
        </button>
      }
    >
      <dl className="facts">
        <dt>Status</dt>
        <dd>
          <StatusBadge status={appointment.status} />
        </dd>
        <dt>Time</dt>
        <dd>
          {formatTime(appointment.start)}–{formatTime(appointment.end)}
        </dd>
        <dt>Vehicle</dt>
        <dd>
          <Link to={`/vehicles/${appointment.vehicleId}`}>Open vehicle</Link>
        </dd>
        {appointment.workOrderId && (
          <>
            <dt>Work order</dt>
            <dd>
              <Link to={`/work-orders/${appointment.workOrderId}`}>Open work order</Link>
            </dd>
          </>
        )}
        {appointment.cancellationReason && (
          <>
            <dt>Cancelled</dt>
            <dd>{appointment.cancellationReason}</dd>
          </>
        )}
      </dl>
      {canAct && (
        <div className="actions">
          <button type="button" className="primary" onClick={() => checkIn.mutate()} disabled={checkIn.isPending}>
            Check in
          </button>
          <button type="button" onClick={() => noShow.mutate()} disabled={noShow.isPending}>
            No-show
          </button>
          <button type="button" onClick={() => setMoving(!moving)} aria-expanded={moving}>
            {moving ? "Keep the time" : "Reschedule"}
          </button>
          <input placeholder="Reason for cancelling" value={reason} onChange={(e) => setReason(e.target.value)} />
          <button type="button" className="danger" onClick={() => cancel.mutate()} disabled={!reason.trim() || cancel.isPending}>
            Cancel appointment
          </button>
        </div>
      )}
      <ErrorMessage error={checkIn.error ?? noShow.error ?? cancel.error} />
      {canAct && moving && (
        <RescheduleForm
          appointment={appointment}
          onMoved={async (moved) => {
            await refresh();
            onMoved(moved);
          }}
        />
      )}
    </Card>
  );
}

/** Moves a scheduled appointment to another time, another bay, or both; it starts from where the appointment is. */
function RescheduleForm({
  appointment,
  onMoved,
}: {
  appointment: AppointmentDetails;
  onMoved: (moved: AppointmentDetails) => Promise<void>;
}) {
  const bays = useQuery({ queryKey: ["bays"], queryFn: async () => unwrap(await api.GET("/api/bays")) });
  const [form, setForm] = useState({ bayId: appointment.bayId, ...slotFieldsOf(appointment) });
  const move = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST("/api/appointments/{id}/reschedule", {
          params: { path: { id: appointment.id } },
          body: { bayId: form.bayId, ...slotFrom(form) },
        }),
      ),
    onSuccess: onMoved,
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });
  const choices = bays.data?.filter((bay) => bay.isInService || bay.id === appointment.bayId) ?? [];

  return (
    <form
      className="form-grid"
      aria-label="Reschedule appointment"
      onSubmit={(e) => {
        e.preventDefault();
        move.mutate();
      }}
    >
      <Field label="Bay">
        <select required value={form.bayId} onChange={set("bayId")}>
          {choices.map((bay) => (
            <option key={bay.id} value={bay.id}>
              {bay.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label="Day">
        <input type="date" required value={form.date} onChange={set("date")} />
      </Field>
      <Field label="Start" hint="Quarter hours: 08:00, 08:15, …">
        <input type="time" step={900} required value={form.time} onChange={set("time")} />
      </Field>
      <Field label="Hours">
        <input type="number" min={0.25} step={0.25} required value={form.hours} onChange={set("hours")} />
      </Field>
      <div className="actions">
        <button type="submit" className="primary" disabled={move.isPending || bays.isPending}>
          Move appointment
        </button>
      </div>
      <ErrorMessage error={move.error ?? bays.error} />
    </form>
  );
}
