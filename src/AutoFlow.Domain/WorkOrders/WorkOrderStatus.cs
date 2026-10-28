namespace AutoFlow.Domain.WorkOrders;

/// <summary>
/// Lifecycle of a work order. The happy path is
/// Draft → Approved → InProgress → Completed → Invoiced; a work order that the
/// customer declines before any work starts ends as Cancelled.
/// </summary>
public enum WorkOrderStatus
{
    /// <summary>Being estimated; the customer has not agreed yet.</summary>
    Draft = 0,

    /// <summary>The customer agreed to the estimate; work has not started.</summary>
    Approved = 1,

    /// <summary>A mechanic is working on the vehicle.</summary>
    InProgress = 2,

    /// <summary>Work is finished; the lines are final and ready to invoice.</summary>
    Completed = 3,

    /// <summary>An invoice has been issued. Terminal.</summary>
    Invoiced = 4,

    /// <summary>Declined or withdrawn before work started. Terminal.</summary>
    Cancelled = 5,
}
