using AutoFlow.Domain.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Domain.Invoices;

/// <summary>
/// One charged position of an invoice: a copy of a work order line as it was
/// when the invoice was issued. Immutable, like the invoice's other content.
/// </summary>
public sealed class InvoiceLine : Entity
{
    private InvoiceLine(
        Guid id,
        int number,
        WorkOrderLineKind kind,
        string description,
        string? partNumber,
        decimal quantity,
        decimal unitPrice)
        : base(id)
    {
        Number = number;
        Kind = kind;
        Description = description;
        PartNumber = partNumber;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>Position on the invoice, 1-based and without gaps.</summary>
    public int Number { get; }

    public WorkOrderLineKind Kind { get; }

    public string Description { get; }

    public string? PartNumber { get; }

    public decimal Quantity { get; }

    /// <summary>Net price per unit (hourly rate for labour).</summary>
    public decimal UnitPrice { get; }

    /// <summary>Net line total, rounded like the work order line it was copied from.</summary>
    public decimal Total =>
        Math.Round(Quantity * UnitPrice, WorkOrderLine.MoneyDecimals, MidpointRounding.AwayFromZero);

    internal static InvoiceLine CopyOf(WorkOrderLine line, int number) =>
        new(Guid.NewGuid(), number, line.Kind, line.Description, line.PartNumber, line.Quantity, line.UnitPrice);
}
