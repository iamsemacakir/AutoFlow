import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, ifMatch, isStale, unwrap } from "../api/client";
import type { CustomerDetails } from "../api/types";
import { FRONT_DESK, useAuth } from "../auth/AuthContext";
import { Card, Empty, ErrorMessage, Field, Loading, PageHeader, StatusBadge } from "../components/ui";
import { formatDate, formatDateTime, formatMoney, formatNumber } from "../lib/format";

export function CustomerPage() {
  const { id = "" } = useParams();
  const { hasRole } = useAuth();
  const frontDesk = hasRole(...FRONT_DESK);
  const [editing, setEditing] = useState(false);
  const [registering, setRegistering] = useState(false);

  const customer = useQuery({
    queryKey: ["customer", id],
    queryFn: async () => unwrap(await api.GET("/api/customers/{id}", { params: { path: { id } } })),
  });
  const vehicles = useQuery({
    queryKey: ["customer-vehicles", id],
    queryFn: async () =>
      unwrap(await api.GET("/api/customers/{customerId}/vehicles", { params: { path: { customerId: id } } })),
  });

  if (customer.isPending) {
    return <Loading />;
  }

  if (!customer.data) {
    return <ErrorMessage error={customer.error} />;
  }

  const { data } = customer;

  return (
    <>
      <PageHeader
        title={`${data.firstName} ${data.lastName}`}
        subtitle={<Link to="/customers">← All customers</Link>}
        actions={
          frontDesk && (
            <button type="button" onClick={() => setEditing(!editing)}>
              {editing ? "Close" : "Edit details"}
            </button>
          )
        }
      />
      <div className="columns">
        <Card title="Contact">
          {editing ? (
            <EditCustomer customer={data} onDone={() => setEditing(false)} />
          ) : (
            <dl className="facts">
              <dt>E-mail</dt>
              <dd>{data.email}</dd>
              <dt>Phone</dt>
              <dd>{data.phoneNumber ?? "—"}</dd>
              <dt>Customer since</dt>
              <dd>{formatDate(data.createdAt.slice(0, 10))}</dd>
            </dl>
          )}
          {frontDesk && !editing && <DeleteCustomer customer={data} />}
        </Card>
        <Card
          title="Vehicles"
          actions={
            frontDesk && (
              <button type="button" onClick={() => setRegistering(!registering)}>
                {registering ? "Close" : "Register vehicle"}
              </button>
            )
          }
        >
          {registering && <RegisterVehicle customerId={id} />}
          <ErrorMessage error={vehicles.error} />
          {vehicles.data?.length === 0 && <Empty>No vehicles registered.</Empty>}
          {vehicles.data && vehicles.data.length > 0 && (
            <table>
              <thead>
                <tr>
                  <th>Vehicle</th>
                  <th>Plate</th>
                  <th className="number">Mileage</th>
                </tr>
              </thead>
              <tbody>
                {vehicles.data.map((vehicle) => (
                  <tr key={vehicle.id}>
                    <td>
                      <Link to={`/vehicles/${vehicle.id}`}>
                        {vehicle.make} {vehicle.model} ({vehicle.modelYear})
                      </Link>
                    </td>
                    <td>{vehicle.licensePlate}</td>
                    <td className="number">{formatNumber(vehicle.mileageKm)} km</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      </div>
      <WorkOrders customerId={id} vehicles={vehicles.data ?? []} />
      {frontDesk && <Invoices customerId={id} />}
    </>
  );
}

type OwnedVehicle = { id: string; make: string; model: string; licensePlate: string };

/**
 * Everything the customer commissioned, newest first and in every status:
 * what waits for their approval, what is in the workshop, what is done. Work
 * orders for cars they have since sold stay listed under the customer who
 * ordered them; those link to the vehicle without naming it.
 */
function WorkOrders({ customerId, vehicles }: { customerId: string; vehicles: OwnedVehicle[] }) {
  const workOrders = useQuery({
    queryKey: ["customer-work-orders", customerId],
    queryFn: async () =>
      unwrap(await api.GET("/api/work-orders", { params: { query: { customerId, page: 1, pageSize: 50 } } })),
  });
  const vehicleName = (vehicleId: string) => {
    const vehicle = vehicles.find((owned) => owned.id === vehicleId);
    return vehicle ? `${vehicle.make} ${vehicle.model} (${vehicle.licensePlate})` : "Earlier vehicle";
  };

  return (
    <Card title="Work orders">
      <ErrorMessage error={workOrders.error} />
      {workOrders.data?.items.length === 0 && <Empty>No work orders yet.</Empty>}
      {workOrders.data && workOrders.data.items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Opened</th>
              <th>Complaint</th>
              <th>Vehicle</th>
              <th>Status</th>
              <th className="number">Total (net)</th>
            </tr>
          </thead>
          <tbody>
            {workOrders.data.items.map((workOrder) => (
              <tr key={workOrder.id}>
                <td>{formatDateTime(workOrder.createdAt)}</td>
                <td>
                  <Link to={`/work-orders/${workOrder.id}`}>{workOrder.complaint}</Link>
                </td>
                <td>
                  <Link to={`/vehicles/${workOrder.vehicleId}`}>{vehicleName(workOrder.vehicleId)}</Link>
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
      {workOrders.data && workOrders.data.totalCount > workOrders.data.items.length && (
        <p className="muted">
          The latest {workOrders.data.items.length} of {workOrders.data.totalCount} work orders.
        </p>
      )}
    </Card>
  );
}

/**
 * The customer's invoices, newest number first, with what they still owe and
 * how much of it is past due. Invoices are front-desk business, so mechanics
 * neither see nor request them. The sum is only given when every invoice is
 * on the page; otherwise it would quietly leave money out.
 */
function Invoices({ customerId }: { customerId: string }) {
  const invoices = useQuery({
    queryKey: ["customer-invoices", customerId],
    queryFn: async () =>
      unwrap(await api.GET("/api/invoices", { params: { query: { customerId, page: 1, pageSize: 50 } } })),
  });
  const items = invoices.data?.items ?? [];
  const complete = invoices.data !== undefined && invoices.data.totalCount === items.length;
  const owed = items.reduce((sum, invoice) => sum + invoice.balance, 0);
  const overdue = items.filter((invoice) => invoice.isOverdue).reduce((sum, invoice) => sum + invoice.balance, 0);

  return (
    <Card title="Invoices">
      <ErrorMessage error={invoices.error} />
      {invoices.data && items.length === 0 && <Empty>No invoices yet.</Empty>}
      {complete && items.length > 0 && (
        <p className={overdue > 0 ? "warning" : "muted"}>
          {owed === 0
            ? "Everything is paid."
            : `Owes ${formatMoney(owed)}${overdue > 0 ? `, ${formatMoney(overdue)} of it overdue` : ""}.`}
        </p>
      )}
      {items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Number</th>
              <th>Vehicle</th>
              <th>Due</th>
              <th>Status</th>
              <th className="number">Total</th>
              <th className="number">Balance</th>
            </tr>
          </thead>
          <tbody>
            {items.map((invoice) => (
              <tr key={invoice.id}>
                <td>
                  <Link to={`/invoices/${invoice.id}`}>{invoice.number}</Link>
                </td>
                <td>{invoice.vehicleDescription}</td>
                <td className={invoice.isOverdue ? "warning" : undefined}>
                  {formatDate(invoice.dueDate)}
                  {invoice.isOverdue && " · overdue"}
                </td>
                <td>
                  <StatusBadge status={invoice.status} />
                </td>
                <td className="number">{formatMoney(invoice.grossTotal)}</td>
                <td className="number">{formatMoney(invoice.balance)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {invoices.data && !complete && (
        <p className="muted">
          The latest {items.length} of {invoices.data.totalCount} invoices.
        </p>
      )}
    </Card>
  );
}

function EditCustomer({ customer, onDone }: { customer: CustomerDetails; onDone: () => void }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    firstName: customer.firstName,
    lastName: customer.lastName,
    email: customer.email,
    phoneNumber: customer.phoneNumber ?? "",
  });
  const save = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT("/api/customers/{id}", {
          params: { path: { id: customer.id } },
          body: { ...form, phoneNumber: form.phoneNumber.trim() || null },
          headers: ifMatch(`/api/customers/${customer.id}`),
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["customer", customer.id] });
      onDone();
    },
    // A colleague saved in the meantime: refresh what the page shows; the form keeps
    // what was typed, and saving again (now against their version) is a deliberate choice.
    onError: (error) => isStale(error) && queryClient.invalidateQueries({ queryKey: ["customer", customer.id] }),
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });

  return (
    <form
      className="form-grid"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      <Field label="First name">
        <input required value={form.firstName} onChange={set("firstName")} />
      </Field>
      <Field label="Last name">
        <input required value={form.lastName} onChange={set("lastName")} />
      </Field>
      <Field label="E-mail">
        <input type="email" required value={form.email} onChange={set("email")} />
      </Field>
      <Field label="Phone">
        <input value={form.phoneNumber} onChange={set("phoneNumber")} />
      </Field>
      <div className="actions">
        <button type="submit" className="primary" disabled={save.isPending}>
          Save
        </button>
      </div>
      <ErrorMessage error={save.error} />
    </form>
  );
}

function RegisterVehicle({ customerId }: { customerId: string }) {
  const navigate = useNavigate();
  const [form, setForm] = useState({ vin: "", licensePlate: "", make: "", model: "", modelYear: "", mileageKm: "" });
  const register = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST("/api/customers/{customerId}/vehicles", {
          params: { path: { customerId } },
          body: { ...form, modelYear: Number(form.modelYear), mileageKm: Number(form.mileageKm) },
        }),
      ),
    onSuccess: (vehicle) => navigate(`/vehicles/${vehicle.id}`),
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });

  return (
    <form
      className="form-grid"
      onSubmit={(e) => {
        e.preventDefault();
        register.mutate();
      }}
    >
      <Field label="VIN">
        <input required minLength={17} maxLength={17} value={form.vin} onChange={set("vin")} />
      </Field>
      <Field label="License plate">
        <input required value={form.licensePlate} onChange={set("licensePlate")} />
      </Field>
      <Field label="Make">
        <input required value={form.make} onChange={set("make")} />
      </Field>
      <Field label="Model">
        <input required value={form.model} onChange={set("model")} />
      </Field>
      <Field label="Model year">
        <input type="number" required value={form.modelYear} onChange={set("modelYear")} />
      </Field>
      <Field label="Mileage (km)">
        <input type="number" min={0} required value={form.mileageKm} onChange={set("mileageKm")} />
      </Field>
      <div className="actions">
        <button type="submit" className="primary" disabled={register.isPending}>
          Register vehicle
        </button>
      </div>
      <ErrorMessage error={register.error} />
    </form>
  );
}

/**
 * Removes a customer after a second click. The API refuses while they still
 * own vehicles or have work orders or appointments; its reason is shown.
 */
function DeleteCustomer({ customer }: { customer: CustomerDetails }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  const remove = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.DELETE("/api/customers/{id}", {
          params: { path: { id: customer.id } },
          headers: ifMatch(`/api/customers/${customer.id}`),
        }),
      ),
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: ["customer", customer.id] });
      await queryClient.invalidateQueries({ queryKey: ["customers"] });
      navigate("/customers");
    },
    onError: (error) => isStale(error) && queryClient.invalidateQueries({ queryKey: ["customer", customer.id] }),
  });
  const name = `${customer.firstName} ${customer.lastName}`;

  return (
    <>
      <div className="actions">
        {confirming ? (
          <>
            <button type="button" className="danger" onClick={() => remove.mutate()} disabled={remove.isPending}>
              Delete {name} for good
            </button>
            <button
              type="button"
              onClick={() => {
                setConfirming(false);
                remove.reset();
              }}
            >
              Keep
            </button>
          </>
        ) : (
          <button type="button" className="link" onClick={() => setConfirming(true)}>
            Delete customer
          </button>
        )}
      </div>
      <ErrorMessage error={remove.error} />
    </>
  );
}
