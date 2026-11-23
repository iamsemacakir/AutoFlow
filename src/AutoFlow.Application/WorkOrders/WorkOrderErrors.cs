using AutoFlow.Application.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.WorkOrders;

public static class WorkOrderErrors
{
    public static Error NotFound(Guid workOrderId) =>
        Error.NotFound("work_order.not_found", $"Work order '{workOrderId}' was not found.");

    public static Error LineNotFound(Guid workOrderId, Guid lineId) =>
        Error.NotFound(
            "work_order.line_not_found",
            $"Work order '{workOrderId}' has no line '{lineId}'.");

    public static Error UnknownStatus(string status) =>
        Error.Validation(
            "work_order.unknown_status",
            $"'{status}' is not a work order status; use one of: {string.Join(", ", Enum.GetNames<WorkOrderStatus>())}.");
}
