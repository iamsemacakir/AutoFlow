import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api, unwrap } from "../api/client";
import { Card, Empty, ErrorMessage, Loading, PageHeader, StatusBadge } from "../components/ui";
import { formatDate, formatMoney } from "../lib/format";

const statuses = ["", "Issued", "PartiallyPaid", "Paid"] as const;

/** The address of one view of the list: only the filters that are set, and the page beyond the first. */
function viewParams(status: string, overdue: boolean, page = 1): Record<string, string> {
  return {
    ...(status ? { status } : {}),
    ...(overdue ? { overdue: "true" } : {}),
    ...(page > 1 ? { page: String(page) } : {}),
  };
}

/**
 * Every invoice, newest number first, by status and, for the money to
 * chase, only the overdue ones. Filters and page live in the address, so
 * the dunning list can be bookmarked.
 */
export function InvoicesPage() {
  const [params, setParams] = useSearchParams();
  const status = params.get("status") ?? "";
  const overdue = params.get("overdue") === "true";
  const page = Number(params.get("page") ?? "1");

  const invoices = useQuery({
    queryKey: ["invoices", status, overdue, page],
    queryFn: async () =>
      unwrap(
        await api.GET("/api/invoices", {
          params: { query: { status: status || undefined, overdue: overdue || undefined, page, pageSize: 20 } },
        }),
      ),
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
            aria-pressed={value === status}
            onClick={() => setParams(viewParams(value, overdue))}
          >
            {value ? value.replace("PartiallyPaid", "Partially paid") : "All"}
          </button>
        ))}
      </div>
      <div className="toolbar" role="group" aria-label="Filter by due date">
        <button
          type="button"
          className={overdue ? "selected" : undefined}
          aria-pressed={overdue}
          onClick={() => setParams(viewParams(status, !overdue))}
        >
          Only overdue
        </button>
      </div>
      <ErrorMessage error={invoices.error} />
      {invoices.isPending && <Loading />}
      {invoices.data && (
        <Card>
          {invoices.data.items.length === 0 ? (
            <Empty>{overdue ? "No overdue invoices." : "No invoices."}</Empty>
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
              <button type="button" disabled={page <= 1} onClick={() => setParams(viewParams(status, overdue, page - 1))}>
                Previous
              </button>
              <span className="muted">
                Page {page} of {(invoices.data.totalPages ?? 1)}
              </span>
              <button
                type="button"
                disabled={page >= (invoices.data.totalPages ?? 1)}
                onClick={() => setParams(viewParams(status, overdue, page + 1))}
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
