using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.WorkOrders;

/// <summary>
/// Read model of a work order returned by the work order use cases. Status and
/// line kind are their names (<c>"InProgress"</c>, <c>"Part"</c>), the same
/// values the database stores, so clients never depend on enum numbers.
/// </summary>
public sealed record WorkOrderDetails(
    Guid Id,
    Guid VehicleId,
    Guid CustomerId,
    string Complaint,
    int MileageKmAtIntake,
    string Status,
    IReadOnlyList<WorkOrderLineDetails> Lines,
    decimal LabourTotal,
    decimal PartsTotal,
    decimal Total,
    decimal? ApprovedTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? InvoicedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason)
{
    public static WorkOrderDetails From(WorkOrder workOrder)
    {
        ArgumentNullException.ThrowIfNull(workOrder);

        return new(
            workOrder.Id,
            workOrder.VehicleId,
            workOrder.CustomerId,
            workOrder.Complaint,
            workOrder.MileageKmAtIntake,
            workOrder.Status.ToString(),
            [.. workOrder.Lines.Select(WorkOrderLineDetails.From)],
            workOrder.LabourTotal,
            workOrder.PartsTotal,
            workOrder.Total,
            workOrder.ApprovedTotal,
            workOrder.CreatedAt,
            workOrder.ApprovedAt,
            workOrder.StartedAt,
            workOrder.CompletedAt,
            workOrder.InvoicedAt,
            workOrder.CancelledAt,
            workOrder.CancellationReason);
    }
}

/// <summary>One line of a <see cref="WorkOrderDetails"/>, in work order line-number order.</summary>
public sealed record WorkOrderLineDetails(
    Guid Id,
    int Number,
    string Kind,
    string Description,
    string? PartNumber,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total)
{
    public static WorkOrderLineDetails From(WorkOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new(
            line.Id,
            line.Number,
            line.Kind.ToString(),
            line.Description,
            line.PartNumber,
            line.Quantity,
            line.UnitPrice,
            line.Total);
    }
}
