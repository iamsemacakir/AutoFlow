using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.Tests.WorkOrders;

public class WorkOrderStatusHandlerTests
{
    private static readonly DateTimeOffset Now = FixedTimeProvider.Default.AddHours(3);

    private readonly InMemoryStore _store = new();
    private readonly ApproveWorkOrderHandler _approve;
    private readonly StartWorkOrderHandler _start;
    private readonly CompleteWorkOrderHandler _complete;
    private readonly CancelWorkOrderHandler _cancel;

    public WorkOrderStatusHandlerTests()
    {
        var time = new FixedTimeProvider(Now);
        _approve = new ApproveWorkOrderHandler(_store, _store, time);
        _start = new StartWorkOrderHandler(_store, _store, time);
        _complete = new CompleteWorkOrderHandler(_store, _store, time);
        _cancel = new CancelWorkOrderHandler(_store, _store, time);
    }

    [Fact]
    public async Task Approve_records_the_time_and_the_agreed_total()
    {
        var workOrder = SeedWorkOrder(w => w.AddLabour("Diagnosis", 0.5m, 95m));

        var result = await _approve.HandleAsync(new ApproveWorkOrderCommand(workOrder.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value.Status);
        Assert.Equal(Now, result.Value.ApprovedAt);
        Assert.Equal(47.50m, result.Value.ApprovedTotal);
        Assert.Equal(WorkOrderStatus.Approved, workOrder.Status);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Approve_without_lines_throws_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder();

        await Assert.ThrowsAsync<DomainException>(() =>
            _approve.HandleAsync(new ApproveWorkOrderCommand(workOrder.Id)));

        Assert.Equal(WorkOrderStatus.Draft, workOrder.Status);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Start_moves_an_approved_work_order_to_in_progress()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.Approve(FixedTimeProvider.Default);
        });

        var result = await _start.HandleAsync(new StartWorkOrderCommand(workOrder.Id));

        Assert.Equal("InProgress", result.Value.Status);
        Assert.Equal(Now, result.Value.StartedAt);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Start_of_a_draft_throws_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder(w => w.AddLabour("Diagnosis", 0.5m, 95m));

        await Assert.ThrowsAsync<DomainException>(() =>
            _start.HandleAsync(new StartWorkOrderCommand(workOrder.Id)));

        Assert.Equal(WorkOrderStatus.Draft, workOrder.Status);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Complete_finishes_a_work_order_in_progress()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.Approve(FixedTimeProvider.Default);
            w.Start(FixedTimeProvider.Default);
        });

        var result = await _complete.HandleAsync(new CompleteWorkOrderCommand(workOrder.Id));

        Assert.Equal("Completed", result.Value.Status);
        Assert.Equal(Now, result.Value.CompletedAt);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Complete_of_an_approved_work_order_throws_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.Approve(FixedTimeProvider.Default);
        });

        await Assert.ThrowsAsync<DomainException>(() =>
            _complete.HandleAsync(new CompleteWorkOrderCommand(workOrder.Id)));

        Assert.Equal(WorkOrderStatus.Approved, workOrder.Status);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Cancel_records_the_reason_and_the_time()
    {
        var workOrder = SeedWorkOrder();

        var result = await _cancel.HandleAsync(new CancelWorkOrderCommand(workOrder.Id, " Customer declined the estimate "));

        Assert.Equal("Cancelled", result.Value.Status);
        Assert.Equal("Customer declined the estimate", result.Value.CancellationReason);
        Assert.Equal(Now, result.Value.CancelledAt);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Cancel_once_work_has_started_throws_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.Approve(FixedTimeProvider.Default);
            w.Start(FixedTimeProvider.Default);
        });

        await Assert.ThrowsAsync<DomainException>(() =>
            _cancel.HandleAsync(new CancelWorkOrderCommand(workOrder.Id, "Customer changed their mind")));

        Assert.Equal(WorkOrderStatus.InProgress, workOrder.Status);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Every_status_handler_returns_not_found_for_an_unknown_work_order()
    {
        var id = Guid.NewGuid();

        var results = new[]
        {
            await _approve.HandleAsync(new ApproveWorkOrderCommand(id)),
            await _start.HandleAsync(new StartWorkOrderCommand(id)),
            await _complete.HandleAsync(new CompleteWorkOrderCommand(id)),
            await _cancel.HandleAsync(new CancelWorkOrderCommand(id, "Customer declined")),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
            Assert.Equal("work_order.not_found", result.Error.Code);
        });
        Assert.Equal(0, _store.SaveCount);
    }

    private WorkOrder SeedWorkOrder(Action<WorkOrder>? prepare = null)
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
        var workOrder = TestData.WorkOrder(vehicle);
        prepare?.Invoke(workOrder);
        return _store.Seed(workOrder);
    }
}
