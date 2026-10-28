using AutoFlow.Domain.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Domain.Tests.WorkOrders;

public class WorkOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static WorkOrder NewDraft() =>
        WorkOrder.Open(VehicleId, CustomerId, "  Squeaking brakes at the front ", 62_500, Now);

    private static WorkOrder NewDraftWithLine()
    {
        var workOrder = NewDraft();
        workOrder.AddLabour("Replace front brake pads", 1.5m, 95m);
        return workOrder;
    }

    private static WorkOrder InStatus(WorkOrderStatus status)
    {
        var workOrder = NewDraftWithLine();

        if (status == WorkOrderStatus.Cancelled)
        {
            workOrder.Cancel("Customer declined the estimate", Now);
            return workOrder;
        }

        if (status >= WorkOrderStatus.Approved)
        {
            workOrder.Approve(Now.AddHours(1));
        }

        if (status >= WorkOrderStatus.InProgress)
        {
            workOrder.Start(Now.AddHours(2));
        }

        if (status >= WorkOrderStatus.Completed)
        {
            workOrder.Complete(Now.AddHours(4));
        }

        if (status >= WorkOrderStatus.Invoiced)
        {
            workOrder.MarkInvoiced(Now.AddHours(5));
        }

        Assert.Equal(status, workOrder.Status);
        return workOrder;
    }

    [Fact]
    public void Open_creates_draft_with_trimmed_complaint_and_no_lines()
    {
        var workOrder = NewDraft();

        Assert.NotEqual(Guid.Empty, workOrder.Id);
        Assert.Equal(VehicleId, workOrder.VehicleId);
        Assert.Equal(CustomerId, workOrder.CustomerId);
        Assert.Equal("Squeaking brakes at the front", workOrder.Complaint);
        Assert.Equal(62_500, workOrder.MileageKmAtIntake);
        Assert.Equal(WorkOrderStatus.Draft, workOrder.Status);
        Assert.Equal(Now, workOrder.CreatedAt);
        Assert.Empty(workOrder.Lines);
        Assert.Equal(0m, workOrder.Total);
        Assert.True(workOrder.IsEditable);
        Assert.Null(workOrder.ApprovedAt);
        Assert.Null(workOrder.ApprovedTotal);
    }

    [Fact]
    public void Open_requires_vehicle_and_customer()
    {
        Assert.Throws<DomainException>(() => WorkOrder.Open(Guid.Empty, CustomerId, "Service", 0, Now));
        Assert.Throws<DomainException>(() => WorkOrder.Open(VehicleId, Guid.Empty, "Service", 0, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_requires_complaint(string complaint)
    {
        Assert.Throws<DomainException>(() => WorkOrder.Open(VehicleId, CustomerId, complaint, 0, Now));
    }

    [Fact]
    public void Open_rejects_overlong_complaint()
    {
        var complaint = new string('x', WorkOrder.ComplaintMaxLength + 1);

        Assert.Throws<DomainException>(() => WorkOrder.Open(VehicleId, CustomerId, complaint, 0, Now));
    }

    [Fact]
    public void Open_rejects_negative_mileage()
    {
        Assert.Throws<DomainException>(() => WorkOrder.Open(VehicleId, CustomerId, "Service", -1, Now));
    }

    [Fact]
    public void Lines_add_up_to_labour_parts_and_total()
    {
        var workOrder = NewDraft();

        workOrder.AddLabour("Replace front brake pads", 1.5m, 95m);
        workOrder.AddLabour("Test drive", 0.25m, 95m);
        workOrder.AddPart("bp-1234", "Brake pad set, front", 1m, 64.90m);
        workOrder.AddPart(null, "Brake cleaner", 2m, 4.99m);

        Assert.Equal(4, workOrder.Lines.Count);
        Assert.Equal(166.25m, workOrder.LabourTotal); // 142.50 + 23.75
        Assert.Equal(74.88m, workOrder.PartsTotal);   // 64.90 + 9.98
        Assert.Equal(241.13m, workOrder.Total);
    }

    [Fact]
    public void AddLabour_creates_labour_line_without_part_number()
    {
        var line = NewDraft().AddLabour(" Oil change ", 0.5m, 90m);

        Assert.NotEqual(Guid.Empty, line.Id);
        Assert.Equal(WorkOrderLineKind.Labour, line.Kind);
        Assert.Equal("Oil change", line.Description);
        Assert.Null(line.PartNumber);
        Assert.Equal(0.5m, line.Quantity);
        Assert.Equal(90m, line.UnitPrice);
        Assert.Equal(45m, line.Total);
    }

    [Fact]
    public void AddPart_normalizes_part_number_to_upper_case()
    {
        var line = NewDraft().AddPart(" oc-90 ", "Oil filter", 1m, 12.50m);

        Assert.Equal(WorkOrderLineKind.Part, line.Kind);
        Assert.Equal("OC-90", line.PartNumber);
    }

    [Fact]
    public void Line_total_rounds_half_away_from_zero_to_cents()
    {
        // 0.25 h × 10.10 = 2.525 → 2.53
        var line = NewDraft().AddLabour("Diagnosis", 0.25m, 10.10m);

        Assert.Equal(2.53m, line.Total);
    }

    [Fact]
    public void Lines_accept_zero_price_for_goodwill_work()
    {
        var line = NewDraft().AddLabour("Goodwill inspection", 0.5m, 0m);

        Assert.Equal(0m, line.Total);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10000.01")]
    [InlineData("1.255")] // more than two decimal places
    public void Lines_reject_invalid_quantity(string quantity)
    {
        var workOrder = NewDraft();
        var value = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<DomainException>(() => workOrder.AddLabour("Diagnosis", value, 90m));
        Assert.Throws<DomainException>(() => workOrder.AddPart(null, "Bulb", value, 3m));
        Assert.Empty(workOrder.Lines);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000000.01")]
    [InlineData("9.999")] // fractions of a cent
    public void Lines_reject_invalid_unit_price(string price)
    {
        var workOrder = NewDraft();
        var value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<DomainException>(() => workOrder.AddLabour("Diagnosis", 1m, value));
        Assert.Throws<DomainException>(() => workOrder.AddPart(null, "Bulb", 1m, value));
    }

    [Fact]
    public void Lines_accept_trailing_zeros_beyond_the_precision()
    {
        var line = NewDraft().AddPart(null, "Engine oil 5W-30, litres", 4.500m, 11.900m);

        Assert.Equal(53.55m, line.Total);
    }

    [Fact]
    public void Lines_require_description()
    {
        Assert.Throws<DomainException>(() => NewDraft().AddLabour(" ", 1m, 90m));
        Assert.Throws<DomainException>(() => NewDraft().AddPart("X1", "", 1m, 9m));
    }

    [Fact]
    public void AddLine_rejects_more_than_max_lines()
    {
        var workOrder = NewDraft();
        for (var i = 0; i < WorkOrder.MaxLines; i++)
        {
            workOrder.AddPart(null, $"Item {i}", 1m, 1m);
        }

        Assert.Throws<DomainException>(() => workOrder.AddPart(null, "One too many", 1m, 1m));
        Assert.Equal(WorkOrder.MaxLines, workOrder.Lines.Count);
    }

    [Fact]
    public void RemoveLine_removes_it_from_the_total()
    {
        var workOrder = NewDraft();
        var labour = workOrder.AddLabour("Replace wiper blades", 0.2m, 90m);
        workOrder.AddPart(null, "Wiper blades", 1m, 29.90m);

        workOrder.RemoveLine(labour.Id);

        var remaining = Assert.Single(workOrder.Lines);
        Assert.Equal(WorkOrderLineKind.Part, remaining.Kind);
        Assert.Equal(29.90m, workOrder.Total);
    }

    [Fact]
    public void RemoveLine_rejects_unknown_line()
    {
        var workOrder = NewDraftWithLine();

        Assert.Throws<DomainException>(() => workOrder.RemoveLine(Guid.NewGuid()));
        Assert.Single(workOrder.Lines);
    }

    [Fact]
    public void Happy_path_records_each_step()
    {
        var workOrder = NewDraftWithLine();

        workOrder.Approve(Now.AddHours(1));
        workOrder.Start(Now.AddHours(2));
        workOrder.Complete(Now.AddHours(4));
        workOrder.MarkInvoiced(Now.AddHours(5));

        Assert.Equal(WorkOrderStatus.Invoiced, workOrder.Status);
        Assert.Equal<DateTimeOffset?>(Now.AddHours(1), workOrder.ApprovedAt);
        Assert.Equal<DateTimeOffset?>(Now.AddHours(2), workOrder.StartedAt);
        Assert.Equal<DateTimeOffset?>(Now.AddHours(4), workOrder.CompletedAt);
        Assert.Equal<DateTimeOffset?>(Now.AddHours(5), workOrder.InvoicedAt);
        Assert.Null(workOrder.CancelledAt);
    }

    [Fact]
    public void Approve_requires_at_least_one_line()
    {
        var workOrder = NewDraft();

        Assert.Throws<DomainException>(() => workOrder.Approve(Now));
        Assert.Equal(WorkOrderStatus.Draft, workOrder.Status);
    }

    [Fact]
    public void Approve_records_agreed_total_and_later_lines_show_as_deviation()
    {
        var workOrder = NewDraftWithLine(); // 1.5 h × 95 = 142.50
        workOrder.Approve(Now);
        workOrder.Start(Now.AddHours(1));

        workOrder.AddPart("BD-77", "Brake disc, front", 2m, 48m);

        Assert.Equal<decimal?>(142.50m, workOrder.ApprovedTotal);
        Assert.Equal(238.50m, workOrder.Total);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Draft)]
    [InlineData(WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStatus.InProgress)]
    public void Open_work_orders_accept_line_and_complaint_changes(WorkOrderStatus status)
    {
        var workOrder = InStatus(status);

        workOrder.AddPart(null, "Bulb H7", 1m, 6.50m);
        workOrder.UpdateComplaint("Brakes squeak; also check left headlight");

        Assert.True(workOrder.IsEditable);
        Assert.Equal(2, workOrder.Lines.Count);
        Assert.Equal("Brakes squeak; also check left headlight", workOrder.Complaint);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Invoiced)]
    [InlineData(WorkOrderStatus.Cancelled)]
    public void Closed_work_orders_reject_any_change(WorkOrderStatus status)
    {
        var workOrder = InStatus(status);
        var lineId = workOrder.Lines[0].Id;

        Assert.False(workOrder.IsEditable);
        Assert.Throws<DomainException>(() => workOrder.AddLabour("Extra", 1m, 90m));
        Assert.Throws<DomainException>(() => workOrder.AddPart(null, "Extra", 1m, 9m));
        Assert.Throws<DomainException>(() => workOrder.RemoveLine(lineId));
        Assert.Throws<DomainException>(() => workOrder.UpdateComplaint("Changed"));
        Assert.Single(workOrder.Lines);
    }

    public static TheoryData<WorkOrderStatus, Action<WorkOrder>> InvalidTransitions => new()
    {
        { WorkOrderStatus.Draft, w => w.Start(Now) },
        { WorkOrderStatus.Draft, w => w.Complete(Now) },
        { WorkOrderStatus.Draft, w => w.MarkInvoiced(Now) },
        { WorkOrderStatus.Approved, w => w.Approve(Now) },
        { WorkOrderStatus.Approved, w => w.Complete(Now) },
        { WorkOrderStatus.Approved, w => w.MarkInvoiced(Now) },
        { WorkOrderStatus.InProgress, w => w.Approve(Now) },
        { WorkOrderStatus.InProgress, w => w.Start(Now) },
        { WorkOrderStatus.InProgress, w => w.MarkInvoiced(Now) },
        { WorkOrderStatus.InProgress, w => w.Cancel("Too expensive", Now) },
        { WorkOrderStatus.Completed, w => w.Start(Now) },
        { WorkOrderStatus.Completed, w => w.Complete(Now) },
        { WorkOrderStatus.Completed, w => w.Cancel("Too expensive", Now) },
        { WorkOrderStatus.Invoiced, w => w.MarkInvoiced(Now) },
        { WorkOrderStatus.Invoiced, w => w.Cancel("Too expensive", Now) },
        { WorkOrderStatus.Cancelled, w => w.Approve(Now) },
        { WorkOrderStatus.Cancelled, w => w.Start(Now) },
        { WorkOrderStatus.Cancelled, w => w.Cancel("Again", Now) },
    };

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void Invalid_transitions_are_rejected_and_leave_status_unchanged(
        WorkOrderStatus status,
        Action<WorkOrder> transition)
    {
        var workOrder = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => transition(workOrder));

        Assert.Equal(status, workOrder.Status);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Draft)]
    [InlineData(WorkOrderStatus.Approved)]
    public void Cancel_before_work_starts_records_reason(WorkOrderStatus status)
    {
        var workOrder = InStatus(status);

        workOrder.Cancel("  Customer declined the estimate ", Now.AddDays(1));

        Assert.Equal(WorkOrderStatus.Cancelled, workOrder.Status);
        Assert.Equal("Customer declined the estimate", workOrder.CancellationReason);
        Assert.Equal<DateTimeOffset?>(Now.AddDays(1), workOrder.CancelledAt);
    }

    [Fact]
    public void Cancel_requires_reason()
    {
        var workOrder = NewDraft();

        Assert.Throws<DomainException>(() => workOrder.Cancel(" ", Now));
        Assert.Equal(WorkOrderStatus.Draft, workOrder.Status);
    }
}
