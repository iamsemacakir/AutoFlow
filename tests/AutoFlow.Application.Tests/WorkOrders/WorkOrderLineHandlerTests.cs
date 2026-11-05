using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.WorkOrders;

namespace AutoFlow.Application.Tests.WorkOrders;

public class WorkOrderLineHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly AddLabourLineHandler _addLabour;
    private readonly AddPartLineHandler _addPart;
    private readonly RemoveWorkOrderLineHandler _remove;

    public WorkOrderLineHandlerTests()
    {
        _addLabour = new AddLabourLineHandler(_store, _store);
        _addPart = new AddPartLineHandler(_store, _store);
        _remove = new RemoveWorkOrderLineHandler(_store, _store);
    }

    [Fact]
    public async Task AddLabour_appends_a_labour_line_and_returns_the_new_totals()
    {
        var workOrder = SeedWorkOrder();

        var result = await _addLabour.HandleAsync(
            new AddLabourLineCommand(workOrder.Id, "Replace front brake pads", 1.5m, 95m));

        Assert.True(result.IsSuccess);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(1, line.Number);
        Assert.Equal("Labour", line.Kind);
        Assert.Null(line.PartNumber);
        Assert.Equal(142.50m, line.Total);
        Assert.Equal(142.50m, result.Value.LabourTotal);
        Assert.Equal(142.50m, result.Value.Total);
        Assert.Single(workOrder.Lines);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task AddPart_appends_a_part_line_after_existing_lines()
    {
        var workOrder = SeedWorkOrder(w => w.AddLabour("Diagnosis", 0.5m, 95m));

        var result = await _addPart.HandleAsync(
            new AddPartLineCommand(workOrder.Id, "bp-12", "Brake pads, front", 2m, 32.25m));

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value.Lines,
            labour => Assert.Equal("Labour", labour.Kind),
            part =>
            {
                Assert.Equal(2, part.Number);
                Assert.Equal("Part", part.Kind);
                Assert.Equal("BP-12", part.PartNumber);
                Assert.Equal(64.50m, part.Total);
            });
        Assert.Equal(47.50m, result.Value.LabourTotal);
        Assert.Equal(64.50m, result.Value.PartsTotal);
        Assert.Equal(112.00m, result.Value.Total);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task AddLabour_on_an_approved_work_order_keeps_the_approved_total()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.Approve(FixedTimeProvider.Default);
        });

        var result = await _addLabour.HandleAsync(
            new AddLabourLineCommand(workOrder.Id, "Replace worn brake disc", 1m, 95m));

        Assert.Equal(47.50m, result.Value.ApprovedTotal);
        Assert.Equal(142.50m, result.Value.Total);
    }

    [Fact]
    public async Task AddPart_with_an_invalid_price_throws_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder();

        await Assert.ThrowsAsync<DomainException>(() =>
            _addPart.HandleAsync(new AddPartLineCommand(workOrder.Id, null, "Bulb H7", 1m, 6.505m)));

        Assert.Empty(workOrder.Lines);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task AddLabour_on_a_completed_work_order_throws_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.Approve(FixedTimeProvider.Default);
            w.Start(FixedTimeProvider.Default);
            w.Complete(FixedTimeProvider.Default);
        });

        await Assert.ThrowsAsync<DomainException>(() =>
            _addLabour.HandleAsync(new AddLabourLineCommand(workOrder.Id, "Extra work", 1m, 95m)));

        Assert.Single(workOrder.Lines);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Remove_drops_the_line_and_keeps_the_numbers_of_the_others()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.AddPart(null, "Bulb H7", 1m, 6.50m);
        });

        var result = await _remove.HandleAsync(new RemoveWorkOrderLineCommand(workOrder.Id, workOrder.Lines[0].Id));

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(result.Value.Lines);
        Assert.Equal("Bulb H7", remaining.Description);
        Assert.Equal(2, remaining.Number);
        Assert.Equal(6.50m, result.Value.Total);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Remove_unknown_line_returns_not_found_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder(w => w.AddLabour("Diagnosis", 0.5m, 95m));

        var result = await _remove.HandleAsync(new RemoveWorkOrderLineCommand(workOrder.Id, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("work_order.line_not_found", result.Error.Code);
        Assert.Single(workOrder.Lines);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Remove_from_a_cancelled_work_order_throws_and_saves_nothing()
    {
        var workOrder = SeedWorkOrder(w =>
        {
            w.AddLabour("Diagnosis", 0.5m, 95m);
            w.Cancel("Customer declined", FixedTimeProvider.Default);
        });

        await Assert.ThrowsAsync<DomainException>(() =>
            _remove.HandleAsync(new RemoveWorkOrderLineCommand(workOrder.Id, workOrder.Lines[0].Id)));

        Assert.Single(workOrder.Lines);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Every_line_handler_returns_not_found_for_an_unknown_work_order()
    {
        var id = Guid.NewGuid();

        var results = new[]
        {
            await _addLabour.HandleAsync(new AddLabourLineCommand(id, "Diagnosis", 0.5m, 95m)),
            await _addPart.HandleAsync(new AddPartLineCommand(id, null, "Bulb H7", 1m, 6.50m)),
            await _remove.HandleAsync(new RemoveWorkOrderLineCommand(id, Guid.NewGuid())),
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
