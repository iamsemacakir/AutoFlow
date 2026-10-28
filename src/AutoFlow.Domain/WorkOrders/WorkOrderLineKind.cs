namespace AutoFlow.Domain.WorkOrders;

/// <summary>What a work order line charges for.</summary>
public enum WorkOrderLineKind
{
    /// <summary>Mechanic time: quantity is hours, unit price is the hourly rate.</summary>
    Labour = 0,

    /// <summary>A part or consumable: quantity is pieces (or litres), unit price per unit.</summary>
    Part = 1,
}
