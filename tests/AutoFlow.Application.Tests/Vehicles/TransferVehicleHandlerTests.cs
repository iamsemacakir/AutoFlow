using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.Common;

namespace AutoFlow.Application.Tests.Vehicles;

public class TransferVehicleHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly TransferVehicleHandler _handler;

    public TransferVehicleHandlerTests() => _handler = new TransferVehicleHandler(_store, _store, _store);

    [Fact]
    public async Task Handle_moves_vehicle_to_new_owner()
    {
        var seller = _store.Seed(TestData.Customer("Anna"));
        var buyer = _store.Seed(TestData.Customer("Ben"));
        var vehicle = _store.Seed(TestData.Vehicle(seller.Id));

        var result = await _handler.HandleAsync(new TransferVehicleCommand(vehicle.Id, buyer.Id));

        Assert.Equal(buyer.Id, result.Value.CustomerId);
        Assert.Empty(await _store.ListByCustomerAsync(seller.Id));
        Assert.Single(await _store.ListByCustomerAsync(buyer.Id));
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_unknown_vehicle_returns_not_found()
    {
        var buyer = _store.Seed(TestData.Customer());

        var result = await _handler.HandleAsync(new TransferVehicleCommand(Guid.NewGuid(), buyer.Id));

        Assert.Equal("vehicle.not_found", result.Error.Code);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_unknown_new_owner_returns_not_found_and_keeps_owner()
    {
        var seller = _store.Seed(TestData.Customer());
        var vehicle = _store.Seed(TestData.Vehicle(seller.Id));

        var result = await _handler.HandleAsync(new TransferVehicleCommand(vehicle.Id, Guid.NewGuid()));

        Assert.Equal("customer.not_found", result.Error.Code);
        Assert.Equal(seller.Id, vehicle.CustomerId);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_transfer_to_current_owner_throws()
    {
        var seller = _store.Seed(TestData.Customer());
        var vehicle = _store.Seed(TestData.Vehicle(seller.Id));

        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(new TransferVehicleCommand(vehicle.Id, seller.Id)));

        Assert.Equal(0, _store.SaveCount);
    }
}
