import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, unwrap } from "../api/client";
import { FRONT_DESK, useAuth } from "../auth/AuthContext";
import { Card, Empty, ErrorMessage, Field, Loading, PageHeader, StatusBadge } from "../components/ui";
import { formatDateTime, formatMoney, formatNumber, today } from "../lib/format";
import { slotFrom } from "../lib/schedule";

export function VehiclePage() {
  const { id = "" } = useParams();
  const { hasRole } = useAuth();
  const frontDesk = hasRole(...FRONT_DESK);
  const queryClient = useQueryClient();
  const [mileage, setMileage] = useState("");

  const vehicle = useQuery({
    queryKey: ["vehicle", id],
    queryFn: async () => unwrap(await api.GET("/api/vehicles/{id}", { params: { path: { id } } })),
  });
  const history = useQuery({
    queryKey: ["vehicle-work-orders", id],
    queryFn: async () =>
      unwrap(
        await api.GET("/api/vehicles/{vehicleId}/work-orders", {
          params: { path: { vehicleId: id }, query: { page: 1, pageSize: 50 } },
        }),
      ),
  });
  const recordMileage = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT("/api/vehicles/{id}/mileage", { params: { path: { id } }, body: { mileageKm: Number(mileage) } }),
      ),
    onSuccess: async () => {
      setMileage("");
      await queryClient.invalidateQueries({ queryKey: ["vehicle", id] });
    },
  });

  if (vehicle.isPending) {
    return <Loading />;
  }

  if (!vehicle.data) {
    return <ErrorMessage error={vehicle.error} />;
  }

  const { data } = vehicle;

  return (
    <>
      <PageHeader
        title={`${data.make} ${data.model} (${data.modelYear})`}
        subtitle={<Link to={`/customers/${data.customerId}`}>← Owner</Link>}
      />
      <div className="columns">
        <Card title="Vehicle">
          <dl className="facts">
            <dt>License plate</dt>
            <dd>{data.licensePlate}</dd>
            <dt>VIN</dt>
            <dd className="mono">{data.vin}</dd>
            <dt>Mileage</dt>
            <dd>{formatNumber(data.mileageKm)} km</dd>
          </dl>
          <form
            className="inline-form"
            onSubmit={(e) => {
              e.preventDefault();
              recordMileage.mutate();
            }}
          >
            <input
              type="number"
              min={0}
              placeholder="New odometer reading"
              value={mileage}
              onChange={(e) => setMileage(e.target.value)}
              aria-label="Odometer reading"
            />
            <button type="submit" disabled={!mileage || recordMileage.isPending}>
              Record mileage
            </button>
          </form>
          <ErrorMessage error={recordMileage.error} />
        </Card>
        {frontDesk && <OpenWorkOrder vehicleId={id} />}
      </div>
      {frontDesk && <BookAppointment vehicleId={id} />}
      <Card title="Service history">
        <ErrorMessage error={history.error} />
        {history.data?.items.length === 0 && <Empty>No work orders yet.</Empty>}
        {history.data && history.data.items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Opened</th>
                <th>Complaint</th>
                <th>Status</th>
                <th className="number">Total (net)</th>
              </tr>
            </thead>
            <tbody>
              {history.data.items.map((workOrder) => (
                <tr key={workOrder.id}>
                  <td>{formatDateTime(workOrder.createdAt)}</td>
                  <td>
                    <Link to={`/work-orders/${workOrder.id}`}>{workOrder.complaint}</Link>
                  </td>
                  <td>
                    <StatusBadge status={workOrder.status} />
                  </td>
                  <td className="number">{formatMoney(workOrder.total)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}

function OpenWorkOrder({ vehicleId }: { vehicleId: string }) {
  const navigate = useNavigate();
  const [complaint, setComplaint] = useState("");
  const [mileage, setMileage] = useState("");
  const open = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST("/api/vehicles/{vehicleId}/work-orders", {
          params: { path: { vehicleId } },
          body: { complaint, mileageKm: mileage ? Number(mileage) : null },
        }),
      ),
    onSuccess: (workOrder) => navigate(`/work-orders/${workOrder.id}`),
  });

  return (
    <Card title="Intake">
      <form
        className="form-grid"
        onSubmit={(e) => {
          e.preventDefault();
          open.mutate();
        }}
      >
        <Field label="What the customer reports">
          <textarea required rows={3} value={complaint} onChange={(e) => setComplaint(e.target.value)} />
        </Field>
        <Field label="Odometer (km)" hint="Optional; also recorded on the vehicle.">
          <input type="number" min={0} value={mileage} onChange={(e) => setMileage(e.target.value)} />
        </Field>
        <div className="actions">
          <button type="submit" className="primary" disabled={open.isPending}>
            Open work order
          </button>
        </div>
      </form>
      <ErrorMessage error={open.error} />
    </Card>
  );
}

function BookAppointment({ vehicleId }: { vehicleId: string }) {
  const navigate = useNavigate();
  const bays = useQuery({ queryKey: ["bays"], queryFn: async () => unwrap(await api.GET("/api/bays")) });
  const [form, setForm] = useState({ bayId: "", date: today(), time: "09:00", hours: "1", purpose: "" });
  const book = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST("/api/vehicles/{vehicleId}/appointments", {
          params: { path: { vehicleId } },
          body: { bayId: form.bayId, ...slotFrom(form), purpose: form.purpose },
        }),
      ),
    onSuccess: () => navigate(`/?date=${form.date}`),
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });
  const inService = bays.data?.filter((bay) => bay.isInService) ?? [];

  return (
    <Card title="Book an appointment">
      <form
        className="form-grid"
        onSubmit={(e) => {
          e.preventDefault();
          book.mutate();
        }}
      >
        <Field label="Bay">
          <select required value={form.bayId} onChange={set("bayId")}>
            <option value="">Choose a bay</option>
            {inService.map((bay) => (
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
        <Field label="Purpose">
          <input required value={form.purpose} onChange={set("purpose")} placeholder="Annual service" />
        </Field>
        <div className="actions">
          <button type="submit" className="primary" disabled={book.isPending}>
            Book
          </button>
        </div>
      </form>
      <ErrorMessage error={book.error ?? bays.error} />
    </Card>
  );
}
