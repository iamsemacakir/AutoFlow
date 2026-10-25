using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Tests.Vehicles;

public class ListCustomerVehiclesHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly ListCustomerVehiclesHandler _handler;

    public ListCustomerVehiclesHandlerTests() => _handler = new ListCustomerVehiclesHandler(_store, _store);

    [Fact]
    public async Task Handle_returns_only_the_customers_vehicles_oldest_first()
    {
        var owner = _store.Seed(TestData.Customer("Anna"));
        var other = _store.Seed(TestData.Customer("Ben"));
        var newer = _store.Seed(Vehicle.Register(
            owner.Id, "WVWZZZ1JZXW000002", "M-AB 2", "Audi", "A4", 2022, 10_000, FixedTimeProvider.Default.AddDays(1)));
        var older = _store.Seed(TestData.Vehicle(owner.Id));
        _store.Seed(TestData.Vehicle(other.Id, vin: "WVWZZZ1JZXW000003"));

        var result = await _handler.HandleAsync(new ListCustomerVehiclesQuery(owner.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { VehicleDetails.From(older), VehicleDetails.From(newer) }, result.Value);
    }

    [Fact]
    public async Task Handle_customer_without_vehicles_returns_empty_list()
    {
        var owner = _store.Seed(TestData.Customer());

        var result = await _handler.HandleAsync(new ListCustomerVehiclesQuery(owner.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_unknown_customer_returns_not_found()
    {
        var result = await _handler.HandleAsync(new ListCustomerVehiclesQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("customer.not_found", result.Error.Code);
    }
}
