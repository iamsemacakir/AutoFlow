using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.WorkOrders;

/// <summary>
/// A job on one vehicle: what the customer reported, the labour and parts
/// charged for it, and where it stands in the workshop. The work order is the
/// aggregate root; its lines can only be changed through it, so every rule
/// about lines and status is enforced in one place.
/// </summary>
/// <remarks>
/// Status flow (see ADR 0010):
/// <code>
/// Draft ──Approve──► Approved ──Start──► InProgress ──Complete──► Completed ──MarkInvoiced──► Invoiced
///   │                   │
///   └──────Cancel───────┴──► Cancelled
/// </code>
/// Lines may be added and removed until the work order is completed. The total
/// at approval is kept as <see cref="ApprovedTotal"/> so that work added later
/// (a worn part found during the job) stays visible as a deviation from what
/// the customer agreed to.
/// </remarks>
public sealed class WorkOrder : Entity
{
    public const int ComplaintMaxLength = 2000;
    public const int CancellationReasonMaxLength = 500;
    public const int MaxLines = 100;

    private readonly List<WorkOrderLine> _lines = [];

    private WorkOrder(
        Guid id,
        Guid vehicleId,
        Guid customerId,
        string complaint,
        int mileageKmAtIntake,
        DateTimeOffset createdAt)
        : base(id)
    {
        VehicleId = vehicleId;
        CustomerId = customerId;
        Complaint = complaint;
        MileageKmAtIntake = mileageKmAtIntake;
        CreatedAt = createdAt;
        Status = WorkOrderStatus.Draft;
    }

    public Guid VehicleId { get; }

    /// <summary>
    /// The customer who commissioned the work and will be invoiced. Fixed at
    /// opening: if the vehicle is sold afterwards, the order stays with the
    /// customer who brought it in.
    /// </summary>
    public Guid CustomerId { get; }

    /// <summary>What the customer reported or asked for, in their words.</summary>
    public string Complaint { get; private set; }

    /// <summary>Odometer reading when the vehicle was handed over.</summary>
    public int MileageKmAtIntake { get; }

    public WorkOrderStatus Status { get; private set; }

    public IReadOnlyList<WorkOrderLine> Lines => _lines;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Net total the customer agreed to; null until approved.</summary>
    public decimal? ApprovedTotal { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? InvoicedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public decimal LabourTotal => SumOf(WorkOrderLineKind.Labour);

    public decimal PartsTotal => SumOf(WorkOrderLineKind.Part);

    /// <summary>Net total of all lines (labour plus parts), before VAT.</summary>
    public decimal Total => _lines.Sum(line => line.Total);

    /// <summary>True while lines and the complaint may still change.</summary>
    public bool IsEditable => Status is WorkOrderStatus.Draft or WorkOrderStatus.Approved or WorkOrderStatus.InProgress;

    public static WorkOrder Open(
        Guid vehicleId,
        Guid customerId,
        string complaint,
        int mileageKmAtIntake,
        DateTimeOffset createdAt)
    {
        if (vehicleId == Guid.Empty)
        {
            throw new DomainException("A work order must be for a vehicle.");
        }

        if (customerId == Guid.Empty)
        {
            throw new DomainException("A work order must belong to a customer.");
        }

        if (mileageKmAtIntake < 0)
        {
            throw new DomainException("Mileage cannot be negative.");
        }

        return new WorkOrder(
            Guid.NewGuid(),
            vehicleId,
            customerId,
            Guard.RequiredText(complaint, "Complaint", ComplaintMaxLength),
            mileageKmAtIntake,
            createdAt);
    }

    public void UpdateComplaint(string complaint)
    {
        EnsureEditable();
        Complaint = Guard.RequiredText(complaint, "Complaint", ComplaintMaxLength);
    }

    public WorkOrderLine AddLabour(string description, decimal hours, decimal hourlyRate) =>
        Add(WorkOrderLine.Labour(description, hours, hourlyRate));

    public WorkOrderLine AddPart(string? partNumber, string description, decimal quantity, decimal unitPrice) =>
        Add(WorkOrderLine.Part(partNumber, description, quantity, unitPrice));

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable();

        var line = _lines.Find(l => l.Id == lineId)
            ?? throw new DomainException("The work order has no such line.");

        _lines.Remove(line);
    }

    /// <summary>The customer agrees to the estimate; its total is recorded.</summary>
    public void Approve(DateTimeOffset approvedAt)
    {
        EnsureStatus(WorkOrderStatus.Draft, "approved");

        if (_lines.Count == 0)
        {
            throw new DomainException("A work order needs at least one line before it can be approved.");
        }

        Status = WorkOrderStatus.Approved;
        ApprovedAt = approvedAt;
        ApprovedTotal = Total;
    }

    public void Start(DateTimeOffset startedAt)
    {
        EnsureStatus(WorkOrderStatus.Approved, "started");

        Status = WorkOrderStatus.InProgress;
        StartedAt = startedAt;
    }

    /// <summary>Work is done; from now on the lines are final.</summary>
    public void Complete(DateTimeOffset completedAt)
    {
        EnsureStatus(WorkOrderStatus.InProgress, "completed");

        if (_lines.Count == 0)
        {
            throw new DomainException("A work order needs at least one line before it can be completed.");
        }

        Status = WorkOrderStatus.Completed;
        CompletedAt = completedAt;
    }

    /// <summary>Called when an invoice has been issued for this work order.</summary>
    public void MarkInvoiced(DateTimeOffset invoicedAt)
    {
        EnsureStatus(WorkOrderStatus.Completed, "invoiced");

        Status = WorkOrderStatus.Invoiced;
        InvoicedAt = invoicedAt;
    }

    /// <summary>
    /// Withdraws a work order before any work has been done. Once a mechanic
    /// has started, the time spent has to be completed and charged instead.
    /// </summary>
    public void Cancel(string reason, DateTimeOffset cancelledAt)
    {
        if (Status is not (WorkOrderStatus.Draft or WorkOrderStatus.Approved))
        {
            throw new DomainException($"A work order that is {Describe(Status)} cannot be cancelled.");
        }

        CancellationReason = Guard.RequiredText(reason, "Cancellation reason", CancellationReasonMaxLength);
        Status = WorkOrderStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    private WorkOrderLine Add(WorkOrderLine line)
    {
        EnsureEditable();

        if (_lines.Count >= MaxLines)
        {
            throw new DomainException($"A work order can have at most {MaxLines} lines.");
        }

        _lines.Add(line);
        return line;
    }

    private decimal SumOf(WorkOrderLineKind kind) =>
        _lines.Where(line => line.Kind == kind).Sum(line => line.Total);

    private void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new DomainException($"A work order that is {Describe(Status)} can no longer be changed.");
        }
    }

    private void EnsureStatus(WorkOrderStatus required, string action)
    {
        if (Status != required)
        {
            throw new DomainException(
                $"Only a work order that is {Describe(required)} can be {action}; this one is {Describe(Status)}.");
        }
    }

    private static string Describe(WorkOrderStatus status) => status switch
    {
        WorkOrderStatus.Draft => "a draft",
        WorkOrderStatus.Approved => "approved",
        WorkOrderStatus.InProgress => "in progress",
        WorkOrderStatus.Completed => "completed",
        WorkOrderStatus.Invoiced => "invoiced",
        WorkOrderStatus.Cancelled => "cancelled",
        _ => status.ToString(),
    };
}
