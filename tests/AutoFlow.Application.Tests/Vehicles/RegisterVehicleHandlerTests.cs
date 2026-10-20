using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.Common;

namespace AutoFlow.Application.Tests.Vehicles;

public class RegisterVehicleHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly RegisterVehicleHandler _handler;

    public RegisterVehicleHandlerTests() =>
        _handler = new RegisterVehicleHandler(_store, _store, _store, new FixedTimeProvider());

    private static RegisterVehicleCommand Command(Guid customerId, string vin = TestData.Vin) =>
        new(customerId, vin, "m-ab 123", "Volkswagen", "Golf", 2019, 50_000);

    [Fact]
    public async Task Handle_registers_vehicle_for_existing_customer()
    {
        var customer = _store.Seed(TestData.Customer());

        var result = await _handler.HandleAsync(Command(customer.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(customer.Id, result.Value.CustomerId);
        Assert.Equal("M-AB 123", result.Value.LicensePlate);
        Assert.Equal(FixedTimeProvider.Default, result.Value.RegisteredAt);
        Assert.Single(_store.Vehicles);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_unknown_customer_returns_not_found()
    {
        var result = await _handler.HandleAsync(Command(Guid.NewGuid()));

        Assert.Equal("customer.not_found", result.Error.Code);
        Assert.Empty(_store.Vehicles);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_duplicate_vin_returns_conflict_even_with_different_casing()
    {
        var customer = _store.Seed(TestData.Customer());
        _store.Seed(TestData.Vehicle(customer.Id));

        var result = await _handler.HandleAsync(Command(customer.Id, TestData.Vin.ToLowerInvariant()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal("vehicle.duplicate_vin", result.Error.Code);
        Assert.Single(_store.Vehicles);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_invalid_vin_throws_domain_exception()
    {
        var customer = _store.Seed(TestData.Customer());

        await Assert.ThrowsAsync<DomainException>(() => _handler.HandleAsync(Command(customer.Id, "TOO-SHORT")));

        Assert.Equal(0, _store.SaveCount);
    }
}
