import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, download, ifMatch, isStale, unwrap } from "../api/client";
import { Card, ErrorMessage, Field, Loading, PageHeader, StatusBadge } from "../components/ui";
import { formatDate, formatDateTime, formatMoney, formatNumber, humanize } from "../lib/format";

export function InvoicePage() {
  const { id = "" } = useParams();
  const queryClient = useQueryClient();
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("Card");
  const [downloadError, setDownloadError] = useState<unknown>(null);

  const invoice = useQuery({
    queryKey: ["invoice", id],
    queryFn: async () => unwrap(await api.GET("/api/invoices/{id}", { params: { path: { id } } })),
  });
  const pay = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST("/api/invoices/{id}/payments", {
          params: { path: { id } },
          body: { amount: Number(amount), method, receivedAt: null },
          headers: ifMatch(`/api/invoices/${id}`),
        }),
      ),
    onSuccess: (next) => {
      setAmount("");
      queryClient.setQueryData(["invoice", id], next);
    },
    // A colleague recorded a payment in the meantime: show the balance as it is now
    // rather than taking the same money twice; the typed amount stays in the field.
    onError: (error) => isStale(error) && queryClient.invalidateQueries({ queryKey: ["invoice", id] }),
  });

  if (invoice.isPending) {
    return <Loading />;
  }

  if (!invoice.data) {
    return <ErrorMessage error={invoice.error} />;
  }

  const data = invoice.data;

  return (
    <>
      <PageHeader
        title={`Invoice ${data.number}`}
        subtitle={<Link to="/invoices">← All invoices</Link>}
        actions={
          <>
            <StatusBadge status={data.status} />
            <button
              type="button"
              onClick={() =>
                download(`/api/invoices/${data.id}/pdf`, `invoice-${data.number}.pdf`).catch(setDownloadError)
              }
            >
              Download PDF
            </button>
          </>
        }
      />
      <ErrorMessage error={downloadError} />
      <div className="columns">
        <Card title="Bill to">
          <dl className="facts">
            <dt>Customer</dt>
            <dd>
              <Link to={`/customers/${data.customerId}`}>{data.customerName}</Link>
            </dd>
            <dt>Vehicle</dt>
            <dd>{data.vehicleDescription}</dd>
            <dt>Issued</dt>
            <dd>{formatDateTime(data.issuedAt)}</dd>
            <dt>Due</dt>
            <dd className={data.isOverdue ? "warning" : undefined}>
              {formatDate(data.dueDate)}
              {data.isOverdue && " · overdue"}
            </dd>
            <dt>Work order</dt>
            <dd>
              <Link to={`/work-orders/${data.workOrderId}`}>Open work order</Link>
            </dd>
          </dl>
        </Card>
        <Card title="Totals">
          <dl className="facts totals">
            <dt>Net</dt>
            <dd>{formatMoney(data.netTotal)}</dd>
            <dt>VAT {formatNumber(data.vatRatePercent)} %</dt>
            <dd>{formatMoney(data.vatAmount)}</dd>
            <dt>Total</dt>
            <dd>
              <strong>{formatMoney(data.grossTotal)}</strong>
            </dd>
            <dt>Paid</dt>
            <dd>{formatMoney(data.amountPaid)}</dd>
            <dt>Balance</dt>
            <dd>
              <strong>{formatMoney(data.balance)}</strong>
            </dd>
          </dl>
        </Card>
      </div>
      <Card title="Positions">
        <table>
          <thead>
            <tr>
              <th>#</th>
              <th>Description</th>
              <th className="number">Quantity</th>
              <th className="number">Unit price</th>
              <th className="number">Total</th>
            </tr>
          </thead>
          <tbody>
            {data.lines.map((line) => (
              <tr key={line.number}>
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
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
      <Card title="Payments">
        {data.payments.length > 0 && (
          <table>
            <tbody>
              {data.payments.map((payment) => (
                <tr key={payment.id}>
                  <td>{formatDateTime(payment.receivedAt)}</td>
                  <td>{humanize(payment.method)}</td>
                  <td className="number">{formatMoney(payment.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {data.balance > 0 && (
          <form
            className="form-grid"
            onSubmit={(e) => {
              e.preventDefault();
              pay.mutate();
            }}
          >
            <Field label="Amount received">
              <input type="number" step="0.01" min="0.01" max={data.balance} required value={amount} onChange={(e) => setAmount(e.target.value)} />
            </Field>
            <Field label="Method">
              <select value={method} onChange={(e) => setMethod(e.target.value)}>
                <option value="Card">Card</option>
                <option value="Cash">Cash</option>
                <option value="BankTransfer">Bank transfer</option>
              </select>
            </Field>
            <div className="actions">
              <button type="submit" className="primary" disabled={pay.isPending}>
                Record payment
              </button>
              <button type="button" onClick={() => setAmount(String(data.balance))}>
                Full balance
              </button>
            </div>
          </form>
        )}
        <ErrorMessage error={pay.error} />
      </Card>
    </>
  );
}
