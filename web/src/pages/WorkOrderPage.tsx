import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, ifMatch, isStale, unwrap } from "../api/client";
import type { WorkOrderDetails } from "../api/types";
import { FRONT_DESK, useAuth } from "../auth/AuthContext";
import { Card, Empty, ErrorMessage, Field, Loading, PageHeader, StatusBadge } from "../components/ui";
import { formatDateTime, formatMoney, formatNumber } from "../lib/format";

type Transition = "approve" | "start" | "complete";

/** The next step for each status, and who may take it. */
const nextStep: Partial<Record<string, { action: Transition; label: string; frontDeskOnly: boolean }>> = {
  Draft: { action: "approve", label: "Customer approved the estimate", frontDeskOnly: true },
  Approved: { action: "start", label: "Start work", frontDeskOnly: false },
  InProgress: { action: "complete", label: "Work is done", frontDeskOnly: false },
};

export function WorkOrderPage() {
  const { id = "" } = useParams();
  const { hasRole } = useAuth();
  const frontDesk = hasRole(...FRONT_DESK);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [reason, setReason] = useState("");

  const workOrder = useQuery({
    queryKey: ["work-order", id],
    queryFn: async () => unwrap(await api.GET("/api/work-orders/{id}", { params: { path: { id } } })),
  });

  const update = (next: WorkOrderDetails) => queryClient.setQueryData(["work-order", id], next);
  // Someone changed the work order since it was loaded: show the current one instead.
  const reloadIfStale = (error: unknown) =>
    isStale(error) ? queryClient.invalidateQueries({ queryKey: ["work-order", id] }) : undefined;
  const headers = () => ifMatch(`/api/work-orders/${id}`);

  const transition = useMutation({
    mutationFn: async (action: Transition) => {
      const path = { params: { path: { id } }, headers: headers() };
      switch (action) {
        case "approve":
          return unwrap(await api.POST("/api/work-orders/{id}/approve", path));
        case "start":
          return unwrap(await api.POST("/api/work-orders/{id}/start", path));
        case "complete":
          return unwrap(await api.POST("/api/work-orders/{id}/complete", path));
      }
    },
    onSuccess: update,
    onError: reloadIfStale,
  });
  const cancel = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST("/api/work-orders/{id}/cancel", { params: { path: { id } }, body: { reason }, headers: headers() }),
      ),
    onSuccess: update,
    onError: reloadIfStale,
  });
  const removeLine = useMutation({
    mutationFn: async (lineId: string) =>
      unwrap(
        await api.DELETE("/api/work-orders/{id}/lines/{lineId}", { params: { path: { id, lineId } }, headers: headers() }),
      ),
    onSuccess: update,
    onError: reloadIfStale,
  });
  const issueInvoice = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST("/api/work-orders/{workOrderId}/invoice", { params: { path: { workOrderId: id } } })),
    onSuccess: (invoice) => navigate(`/invoices/${invoice.id}`),
  });

  if (workOrder.isPending) {
    return <Loading />;
  }

  if (!workOrder.data) {
    return <ErrorMessage error={workOrder.error} />;
  }

  const data = workOrder.data;
  const editable = ["Draft", "Approved", "InProgress"].includes(data.status);
  const step = nextStep[data.status];
  const deviation = data.approvedTotal != null && data.total !== data.approvedTotal;

  return (
    <>
      <PageHeader
        title="Work order"
        subtitle={<Link to={`/vehicles/${data.vehicleId}`}>← Vehicle</Link>}
        actions={<StatusBadge status={data.status} />}
      />
      <div className="columns">
        <Card title="Job">
          <p className="complaint">{data.complaint}</p>
          <dl className="facts">
            <dt>Opened</dt>
            <dd>{formatDateTime(data.createdAt)}</dd>
            <dt>Odometer at intake</dt>
            <dd>{formatNumber(data.mileageKmAtIntake)} km</dd>
            {data.approvedAt && (
              <>
                <dt>Approved</dt>
                <dd>{formatDateTime(data.approvedAt)}</dd>
              </>
            )}
            {data.completedAt && (
              <>
                <dt>Completed</dt>
                <dd>{formatDateTime(data.completedAt)}</dd>
              </>
            )}
            {data.cancellationReason && (
              <>
                <dt>Cancelled</dt>
                <dd>{data.cancellationReason}</dd>
              </>
            )}
          </dl>
          <div className="actions">
            {step && (!step.frontDeskOnly || frontDesk) && (
              <button
                type="button"
                className="primary"
                onClick={() => transition.mutate(step.action)}
                disabled={transition.isPending}
              >
                {step.label}
              </button>
            )}
            {data.status === "Completed" && frontDesk && (
              <button type="button" className="primary" onClick={() => issueInvoice.mutate()} disabled={issueInvoice.isPending}>
                Issue invoice
              </button>
            )}
            {data.status === "Invoiced" && frontDesk && <InvoiceLink workOrderId={data.id} />}
          </div>
          {frontDesk && (data.status === "Draft" || data.status === "Approved") && (
            <div className="inline-form">
              <input placeholder="Reason for cancelling" value={reason} onChange={(e) => setReason(e.target.value)} />
              <button type="button" className="danger" onClick={() => cancel.mutate()} disabled={!reason.trim() || cancel.isPending}>
                Cancel work order
              </button>
            </div>
          )}
          <ErrorMessage error={transition.error ?? cancel.error ?? issueInvoice.error} />
        </Card>
        <Card title="Totals (net)">
          <dl className="facts totals">
            <dt>Labour</dt>
            <dd>{formatMoney(data.labourTotal)}</dd>
            <dt>Parts</dt>
            <dd>{formatMoney(data.partsTotal)}</dd>
            <dt>Total</dt>
            <dd>
              <strong>{formatMoney(data.total)}</strong>
            </dd>
            {data.approvedTotal != null && (
              <>
                <dt>Approved</dt>
                <dd className={deviation ? "warning" : undefined}>
                  {formatMoney(data.approvedTotal)}
                  {deviation && " — the total has changed since approval"}
                </dd>
              </>
            )}
          </dl>
        </Card>
      </div>
      <Card title="Lines">
        {data.lines.length === 0 ? (
          <Empty>No labour or parts yet.</Empty>
        ) : (
          <table>
            <thead>
              <tr>
                <th>#</th>
                <th>Description</th>
                <th className="number">Quantity</th>
                <th className="number">Unit price</th>
                <th className="number">Total</th>
                {editable && <th />}
              </tr>
            </thead>
            <tbody>
              {data.lines.map((line) => (
                <tr key={line.id}>
                  <td className="muted">{line.number}</td>
                  <td>
                    {line.description}
                    {line.partNumber && <small className="muted"> · {line.partNumber}</small>}
                  </td>
                  <td className="number">
                    {formatNumber(line.quantity)}
                    {line.kind === "Labour" ? " h" : ""}
                  </td>
                  <td className="number">{formatMoney(line.unitPrice)}</td>
                  <td className="number">{formatMoney(line.total)}</td>
                  {editable && (
                    <td className="number">
                      <button type="button" className="link" onClick={() => removeLine.mutate(line.id)}>
                        Remove
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <ErrorMessage error={removeLine.error} />
        {editable && <AddLine workOrderId={id} onAdded={update} onError={reloadIfStale} />}
      </Card>
    </>
  );
}

function AddLine({
  workOrderId,
  onAdded,
  onError,
}: {
  workOrderId: string;
  onAdded: (next: WorkOrderDetails) => void;
  onError: (error: unknown) => void;
}) {
  const [kind, setKind] = useState<"labour" | "part">("labour");
  const [form, setForm] = useState({ description: "", partNumber: "", quantity: "", price: "" });
  const add = useMutation({
    mutationFn: async () => {
      const path = { params: { path: { id: workOrderId } }, headers: ifMatch(`/api/work-orders/${workOrderId}`) };
      return kind === "labour"
        ? unwrap(
            await api.POST("/api/work-orders/{id}/labour-lines", {
              ...path,
              body: { description: form.description, hours: Number(form.quantity), hourlyRate: Number(form.price) },
            }),
          )
        : unwrap(
            await api.POST("/api/work-orders/{id}/part-lines", {
              ...path,
              body: {
                partNumber: form.partNumber.trim() || null,
                description: form.description,
                quantity: Number(form.quantity),
                unitPrice: Number(form.price),
              },
            }),
          );
    },
    onSuccess: (next) => {
      setForm({ description: "", partNumber: "", quantity: "", price: "" });
      onAdded(next);
    },
    // The form keeps what was typed, so the line can be added again to the newer version.
    onError,
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });

  return (
    <form
      className="form-grid add-line"
      onSubmit={(e) => {
        e.preventDefault();
        add.mutate();
      }}
    >
      <Field label="Kind">
        <select value={kind} onChange={(e) => setKind(e.target.value as "labour" | "part")}>
          <option value="labour">Labour</option>
          <option value="part">Part</option>
        </select>
      </Field>
      <Field label="Description">
        <input required value={form.description} onChange={set("description")} />
      </Field>
      {kind === "part" && (
        <Field label="Part number">
          <input value={form.partNumber} onChange={set("partNumber")} />
        </Field>
      )}
      <Field label={kind === "labour" ? "Hours" : "Quantity"}>
        <input type="number" step="0.01" min="0.01" required value={form.quantity} onChange={set("quantity")} />
      </Field>
      <Field label={kind === "labour" ? "Hourly rate" : "Unit price"}>
        <input type="number" step="0.01" min="0" required value={form.price} onChange={set("price")} />
      </Field>
      <div className="actions">
        <button type="submit" disabled={add.isPending}>
          Add line
        </button>
      </div>
      <ErrorMessage error={add.error} />
    </form>
  );
}

function InvoiceLink({ workOrderId }: { workOrderId: string }) {
  const invoice = useQuery({
    queryKey: ["work-order-invoice", workOrderId],
    queryFn: async () =>
      unwrap(await api.GET("/api/work-orders/{workOrderId}/invoice", { params: { path: { workOrderId } } })),
  });

  return invoice.data ? <Link to={`/invoices/${invoice.data.id}`}>Invoice {invoice.data.number}</Link> : null;
}
