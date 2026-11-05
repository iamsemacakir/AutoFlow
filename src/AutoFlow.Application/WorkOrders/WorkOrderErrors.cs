using AutoFlow.Application.Common;

namespace AutoFlow.Application.WorkOrders;

public static class WorkOrderErrors
{
    public static Error NotFound(Guid workOrderId) =>
        Error.NotFound("work_order.not_found", $"Work order '{workOrderId}' was not found.");

    public static Error LineNotFound(Guid workOrderId, Guid lineId) =>
        Error.NotFound(
            "work_order.line_not_found",
            $"Work order '{workOrderId}' has no line '{lineId}'.");
}
