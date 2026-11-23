import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { api, unwrap } from "../api/client";
import { Card, Empty, ErrorMessage, Loading, PageHeader, StatusBadge } from "../components/ui";
import { formatDateTime, formatMoney, humanize } from "../lib/format";

/** The statuses worth a filter of their own, in the order work moves through them. */
export const workOrderStatuses = ["Draft", "Approved", "InProgress", "Completed", "Invoiced", "Cancelled"] as const;

const pageSize = 20;

/**
 * Every work order of the workshop, newest first: what waits for approval,
 * what is on the lifts and what is done but not invoiced yet, without going
 * vehicle by vehicle. The filter and page live in the address, so a view can
 * be bookmarked and the back button returns to it.
 */
export function WorkOrdersPage() {
  const [params, setParams] = useSearchParams();
  const status = params.get("status") ?? "";
  const page = Number(params.get("page") ?? "1");

  const workOrders = useQuery({
    queryKey: ["work-orders", status, page],
    queryFn: async () =>
      unwrap(await api.GET("/api/work-orders", { params: { query: { status: status || undefined, page, pageSize } } })),
    placeholderData: keepPreviousData,
  });

  const totalPages = workOrders.data?.totalPages ?? 1;
  const goTo = (target: number) => setParams(status ? { status, page: String(target) } : { page: String(target) });

  return (
    <>
      <PageHeader title="Work orders" subtitle="The whole workshop, newest first. Totals are net." />
      <div className="toolbar" role="group" aria-label="Filter by status">
        {["", ...workOrderStatuses].map((value) => (
          <button
            key={value || "all"}
            type="button"
            className={value === status ? "selected" : undefined}
            aria-pressed={value === status}
            onClick={() => setParams(value ? { status: value } : {})}
          >
            {value ? humanize(value) : "All"}
          </button>
        ))}
      </div>
      <ErrorMessage error={workOrders.error} />
      {workOrders.isPending && <Loading />}
      {workOrders.data && (
        <Card>
          {workOrders.data.items.length === 0 ? (
            <Empty>{status ? `No work orders are ${humanize(status).toLowerCase()}.` : "No work orders yet."}</Empty>
          ) : (
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
                      <Link to={`/vehicles/${workOrder.vehicleId}`}>Open vehicle</Link>
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
          {totalPages > 1 && (
            <div className="pager">
              <button type="button" disabled={page <= 1} onClick={() => goTo(page - 1)}>
                Previous
              </button>
              <span className="muted">
                Page {page} of {totalPages}
              </span>
              <button type="button" disabled={page >= totalPages} onClick={() => goTo(page + 1)}>
                Next
              </button>
            </div>
          )}
        </Card>
      )}
    </>
  );
}
