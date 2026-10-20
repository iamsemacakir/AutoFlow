using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Vehicles;
using AutoFlow.Domain.Common;

namespace AutoFlow.Application.Tests.Vehicles;

public class RecordMileageHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly RecordMileageHandler _handler;

    public RecordMileageHandlerTests() => _handler = new RecordMileageHandler(_store, _store);

    [Fact]
    public async Task Handle_records_higher_reading()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid(), mileageKm: 50_000));

        var result = await _handler.HandleAsync(new RecordMileageCommand(vehicle.Id, 61_250));

        Assert.Equal(61_250, result.Value.MileageKm);
        Assert.Equal(61_250, vehicle.MileageKm);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_lower_reading_throws_and_saves_nothing()
    {
        var vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid(), mileageKm: 50_000));

        await Assert.ThrowsAsync<DomainException>(() =>
            _handler.HandleAsync(new RecordMileageCommand(vehicle.Id, 49_000)));

        Assert.Equal(50_000, vehicle.MileageKm);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Handle_unknown_vehicle_returns_not_found()
    {
        var result = await _handler.HandleAsync(new RecordMileageCommand(Guid.NewGuid(), 1_000));

        Assert.Equal("vehicle.not_found", result.Error.Code);
        Assert.Equal(0, _store.SaveCount);
    }
}
