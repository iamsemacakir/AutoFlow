import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api, unwrap } from "../api/client";
import { Card, Empty, ErrorMessage, Loading, PageHeader, StatusBadge } from "../components/ui";
import { formatDate, formatMoney } from "../lib/format";

const statuses = ["", "Issued", "PartiallyPaid", "Paid"] as const;

export function InvoicesPage() {
  const [params, setParams] = useSearchParams();
  const status = params.get("status") ?? "";
  const page = Number(params.get("page") ?? "1");

  const invoices = useQuery({
    queryKey: ["invoices", status, page],
    queryFn: async () =>
      unwrap(await api.GET("/api/invoices", { params: { query: { status: status || undefined, page, pageSize: 20 } } })),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="Invoices" subtitle="Newest first. Amounts include VAT." />
      <div className="toolbar" role="group" aria-label="Filter by status">
        {statuses.map((value) => (
          <button
            key={value || "all"}
            type="button"
            className={value === status ? "selected" : undefined}
            onClick={() => setParams(value ? { status: value } : {})}
          >
            {value ? value.replace("PartiallyPaid", "Partially paid") : "All"}
          </button>
        ))}
      </div>
      <ErrorMessage error={invoices.error} />
      {invoices.isPending && <Loading />}
      {invoices.data && (
        <Card>
          {invoices.data.items.length === 0 ? (
            <Empty>No invoices.</Empty>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Number</th>
                  <th>Customer</th>
                  <th>Due</th>
                  <th>Status</th>
                  <th className="number">Total</th>
                  <th className="number">Balance</th>
                </tr>
              </thead>
              <tbody>
                {invoices.data.items.map((invoice) => (
                  <tr key={invoice.id}>
                    <td>
                      <Link to={`/invoices/${invoice.id}`}>{invoice.number}</Link>
                    </td>
                    <td>{invoice.customerName}</td>
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
          {(invoices.data.totalPages ?? 1) > 1 && (
            <div className="pager">
              <button type="button" disabled={page <= 1} onClick={() => setParams({ status, page: String(page - 1) })}>
                Previous
              </button>
              <span className="muted">
                Page {page} of {(invoices.data.totalPages ?? 1)}
              </span>
              <button
                type="button"
                disabled={page >= (invoices.data.totalPages ?? 1)}
                onClick={() => setParams({ status, page: String(page + 1) })}
              >
                Next
              </button>
            </div>
          )}
        </Card>
      )}
    </>
  );
}
