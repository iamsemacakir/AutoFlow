using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Domain.Common;

namespace AutoFlow.Application.Tests.WorkOrders;

public class OpenWorkOrderHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly OpenWorkOrderHandler _handler;

    public OpenWorkOrderHandlerTests() =>
        _handler = new OpenWorkOrderHandler(_store, _store, _store, new FixedTimeProvider());

    [Fact]
    public async Task Handle_opens_a_draft_for_the_vehicles_current_owner()
    {
        var owner = _store.Seed(TestData.Customer());
        var vehicle = _store.Seed(TestData.Vehicle(owner.Id, mileageKm: 50_000));

        var result = await _handler.HandleAsync(new OpenWorkOrderCommand(vehicle.Id, " Squeaking brakes "));

        Assert.True(result.IsSuccess);
        var details = result.Value;
        Assert.Equal(vehicle.Id, details.VehicleId);
        Assert.Equal(owner.Id, details.CustomerId);
        Assert.Equal("Squeaking brakes", details.Complaint);
        Assert.Equal(50_000, details.MileageKmAtIntake);
        Assert.Equal("Draft", details.Status);
        Assert.Empty(details.Lines);
        Assert.Equal(0m, details.Total);
        Assert.Null(details.ApprovedTotal);
        Assert.Equal(FixedTimeProvider.Default, details.CreatedAt);

        var stored = Assert.Single(_store.WorkOrders);
        Assert.Equal(details.Id, stored.Id);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_takes_the_owner_at_opening_time_after_a_transfer()
    {
        var seller = _store.Seed(TestData.Customer());
        var buyer = _store.Seed(TestData.Customer("Jonas", "Becker"));
        var vehicle = _store.Seed(TestData.Vehicle(seller.Id));
        vehicle.TransferTo(buyer.Id);

        var result = await _handler.HandleAsync(new OpenWorkOrderCommand(vehicle.Id, "Annual service"));

        Assert.Equal(buyer.Id, result.Value.CustomerId);
    }

    [Fact]
    public async Task Handle_records_the_intake_reading_on_the_vehicle()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid(), mileageKm: 50_000));

        var result = await _handler.HandleAsync(new OpenWorkOrderCommand(vehicle.Id, "Annual service", 61_250));

        Assert.Equal(61_250, result.Value.MileageKmAtIntake);
        Assert.Equal(61_250, vehicle.MileageKm);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_rejects_a_reading_below_the_vehicles_mileage_and_saves_nothing()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid(), mileageKm: 50_000));

        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(new OpenWorkOrderCommand(vehicle.Id, "Annual service", 49_000)));

        Assert.Equal(50_000, vehicle.MileageKm);
        Assert.Empty(_store.WorkOrders);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_rejects_a_blank_complaint_and_saves_nothing()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));

        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(new OpenWorkOrderCommand(vehicle.Id, "   ")));

        Assert.Empty(_store.WorkOrders);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_unknown_vehicle_returns_not_found()
    {
        var result = await _handler.HandleAsync(new OpenWorkOrderCommand(Guid.NewGuid(), "Annual service"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("vehicle.not_found", result.Error.Code);
        Assert.Equal(0, _store.SaveCount);
    }
}
