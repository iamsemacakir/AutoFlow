using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.WorkOrders;

namespace AutoFlow.Application.Tests.WorkOrders;

public class ListWorkOrdersHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly ListWorkOrdersHandler _list;

    public ListWorkOrdersHandlerTests()
    {
        _list = new ListWorkOrdersHandler(_store);
    }

    [Fact]
    public async Task Lists_the_work_orders_of_every_vehicle_newest_first()
    {
        var golf = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
        var passat = _store.Seed(TestData.Vehicle(Guid.NewGuid(), vin: "WVWZZZ1JZXW000002"));
        var oldest = _store.Seed(TestData.WorkOrder(golf, FixedTimeProvider.Default));
        var middle = _store.Seed(TestData.WorkOrder(passat, FixedTimeProvider.Default.AddDays(1)));
        var newest = _store.Seed(TestData.WorkOrder(golf, FixedTimeProvider.Default.AddDays(2)));

        var result = await _list.HandleAsync(new ListWorkOrdersQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, result.Value.Items.Select(w => w.Id));
        Assert.Equal(3, result.Value.TotalCount);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("completed")]
    [InlineData(" Completed ")]
    public async Task Filters_by_status_name_ignoring_case(string status)
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
        _store.Seed(TestData.WorkOrder(vehicle));
        var completed = _store.Seed(TestData.CompletedWorkOrder(vehicle));

        var result = await _list.HandleAsync(new ListWorkOrdersQuery(Status: status));

        Assert.True(result.IsSuccess);
        var only = Assert.Single(result.Value.Items);
        Assert.Equal(completed.Id, only.Id);
        Assert.Equal("Completed", only.Status);
    }

    [Fact]
    public async Task Filters_by_the_customer_who_commissioned_the_work_even_after_selling_the_vehicle()
    {
        var seller = Guid.NewGuid();
        var vehicle = _store.Seed(TestData.Vehicle(seller));
        var beforeSale = _store.Seed(TestData.WorkOrder(vehicle, FixedTimeProvider.Default));
        vehicle.TransferTo(Guid.NewGuid());
        _store.Seed(TestData.WorkOrder(vehicle, FixedTimeProvider.Default.AddDays(1)));

        var result = await _list.HandleAsync(new ListWorkOrdersQuery(CustomerId: seller));

        Assert.Equal(beforeSale.Id, Assert.Single(result.Value.Items).Id);
    }

    [Fact]
    public async Task Status_and_customer_filters_combine()
    {
        var customer = Guid.NewGuid();
        var vehicle = _store.Seed(TestData.Vehicle(customer));
        var otherVehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid(), vin: "WVWZZZ1JZXW000002"));
        _store.Seed(TestData.WorkOrder(vehicle));
        var completed = _store.Seed(TestData.CompletedWorkOrder(vehicle));
        _store.Seed(TestData.CompletedWorkOrder(otherVehicle));

        var result = await _list.HandleAsync(new ListWorkOrdersQuery("Completed", customer));

        Assert.Equal(completed.Id, Assert.Single(result.Value.Items).Id);
    }

    [Fact]
    public async Task Unknown_customer_has_no_work_orders()
    {
        _store.Seed(TestData.WorkOrder(_store.Seed(TestData.Vehicle(Guid.NewGuid()))));

        var result = await _list.HandleAsync(new ListWorkOrdersQuery(CustomerId: Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Pages_through_the_work_orders()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
        for (var day = 0; day < 5; day++)
        {
            _store.Seed(TestData.WorkOrder(vehicle, FixedTimeProvider.Default.AddDays(day)));
        }

        var result = await _list.HandleAsync(new ListWorkOrdersQuery(Page: 3, PageSize: 2));

        var only = Assert.Single(result.Value.Items);
        Assert.Equal(FixedTimeProvider.Default, only.CreatedAt);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("2")]
    [InlineData("Draft, Approved")]
    public async Task Unknown_status_is_a_validation_error(string status)
    {
        var result = await _list.HandleAsync(new ListWorkOrdersQuery(Status: status));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal("work_order.unknown_status", result.Error.Code);
        Assert.Contains("InProgress", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 20, "paging.invalid_page")]
    [InlineData(1, 0, "paging.invalid_page_size")]
    [InlineData(1, Paging.MaxPageSize + 1, "paging.invalid_page_size")]
    public async Task Rejects_invalid_paging(int page, int pageSize, string code)
    {
        var result = await _list.HandleAsync(new ListWorkOrdersQuery(Page: page, PageSize: pageSize));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal(code, result.Error.Code);
    }
}
