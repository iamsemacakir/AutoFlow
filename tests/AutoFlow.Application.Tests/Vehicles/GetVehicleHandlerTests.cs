using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Vehicles;

namespace AutoFlow.Application.Tests.Vehicles;

public class GetVehicleHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly GetVehicleHandler _handler;

    public GetVehicleHandlerTests() => _handler = new GetVehicleHandler(_store);

    [Fact]
    public async Task Handle_returns_the_vehicle_details()
    {
        var owner = _store.Seed(TestData.Customer());
        var vehicle = _store.Seed(TestData.Vehicle(owner.Id));

        var result = await _handler.HandleAsync(new GetVehicleQuery(vehicle.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(VehicleDetails.From(vehicle), result.Value);
    }

    [Fact]
    public async Task Handle_unknown_vehicle_returns_not_found()
    {
        var result = await _handler.HandleAsync(new GetVehicleQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("vehicle.not_found", result.Error.Code);
    }
}
