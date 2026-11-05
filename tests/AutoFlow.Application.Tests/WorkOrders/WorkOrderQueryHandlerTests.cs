using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.WorkOrders;

namespace AutoFlow.Application.Tests.WorkOrders;

public class WorkOrderQueryHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly GetWorkOrderHandler _get;
    private readonly ListVehicleWorkOrdersHandler _list;

    public WorkOrderQueryHandlerTests()
    {
        _get = new GetWorkOrderHandler(_store);
        _list = new ListVehicleWorkOrdersHandler(_store, _store);
    }

    [Fact]
    public async Task Get_returns_the_work_order_with_its_lines()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
        var workOrder = TestData.WorkOrder(vehicle);
        workOrder.AddLabour("Diagnosis", 0.5m, 95m);
        _store.Seed(workOrder);

        var result = await _get.HandleAsync(new GetWorkOrderQuery(workOrder.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(workOrder.Id, result.Value.Id);
        Assert.Single(result.Value.Lines);
        Assert.Equal(47.50m, result.Value.Total);
    }

    [Fact]
    public async Task Get_unknown_work_order_returns_not_found()
    {
        var result = await _get.HandleAsync(new GetWorkOrderQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("work_order.not_found", result.Error.Code);
    }

    [Fact]
    public async Task List_returns_the_vehicles_work_orders_newest_first_across_owners()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
        var oldest = _store.Seed(TestData.WorkOrder(vehicle, FixedTimeProvider.Default));
        vehicle.TransferTo(Guid.NewGuid());
        var newest = _store.Seed(TestData.WorkOrder(vehicle, FixedTimeProvider.Default.AddDays(30)));
        var otherVehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid(), vin: "WVWZZZ1JZXW000002"));
        _store.Seed(TestData.WorkOrder(otherVehicle));

        var result = await _list.HandleAsync(new ListVehicleWorkOrdersQuery(vehicle.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { newest.Id, oldest.Id }, result.Value.Items.Select(w => w.Id));
        Assert.NotEqual(result.Value.Items[0].CustomerId, result.Value.Items[1].CustomerId);
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task List_pages_through_the_history()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
        for (var day = 0; day < 5; day++)
        {
            _store.Seed(TestData.WorkOrder(vehicle, FixedTimeProvider.Default.AddDays(day)));
        }

        var result = await _list.HandleAsync(new ListVehicleWorkOrdersQuery(vehicle.Id, Page: 3, PageSize: 2));

        var only = Assert.Single(result.Value.Items);
        Assert.Equal(FixedTimeProvider.Default, only.CreatedAt);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task List_for_a_vehicle_without_work_orders_is_empty()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));

        var result = await _list.HandleAsync(new ListVehicleWorkOrdersQuery(vehicle.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task List_unknown_vehicle_returns_not_found()
    {
        var result = await _list.HandleAsync(new ListVehicleWorkOrdersQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.not_found", result.Error.Code);
    }

    [Theory]
    [InlineData(0, 20, "paging.invalid_page")]
    [InlineData(1, 0, "paging.invalid_page_size")]
    [InlineData(1, Paging.MaxPageSize + 1, "paging.invalid_page_size")]
    public async Task List_rejects_invalid_paging(int page, int pageSize, string code)
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));

        var result = await _list.HandleAsync(new ListVehicleWorkOrdersQuery(vehicle.Id, page, pageSize));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal(code, result.Error.Code);
    }
}
