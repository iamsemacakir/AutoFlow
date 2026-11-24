using AutoFlow.Application.Appointments;
using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Tests.Appointments;

public class ListVehicleAppointmentsHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly ListVehicleAppointmentsHandler _list;
    private readonly Bay _bay;
    private readonly Vehicle _vehicle;

    public ListVehicleAppointmentsHandlerTests()
    {
        _list = new ListVehicleAppointmentsHandler(_store, _store);
        _bay = _store.Seed(TestData.Bay());
        _vehicle = _store.Seed(TestData.Vehicle(Guid.NewGuid()));
    }

    [Fact]
    public async Task Lists_the_vehicles_appointments_latest_start_first_in_every_status()
    {
        var first = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        first.CheckIn(Guid.NewGuid(), TestData.Slot(9, 1).Start);
        var cancelled = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(24 + 9, 1)));
        cancelled.Cancel("Customer is ill", FixedTimeProvider.Default);
        var latest = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(48 + 9, 1)));
        var other = _store.Seed(TestData.Vehicle(Guid.NewGuid(), vin: "WVWZZZ1JZXW000002"));
        _store.Seed(TestData.Appointment(_bay, other, TestData.Slot(72 + 9, 1)));

        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(_vehicle.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal([latest.Id, cancelled.Id, first.Id], result.Value.Items.Select(a => a.Id).ToArray());
        Assert.Equal(["Scheduled", "Cancelled", "CheckedIn"], result.Value.Items.Select(a => a.Status).ToArray());
        Assert.Equal(3, result.Value.TotalCount);
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("scheduled")]
    [InlineData(" Scheduled ")]
    public async Task Filters_by_status_name_ignoring_case(string status)
    {
        var settled = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        settled.MarkNoShow(TestData.Slot(9, 1).Start);
        var upcoming = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(24 + 9, 1)));

        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(_vehicle.Id, Status: status));

        Assert.True(result.IsSuccess);
        Assert.Equal(upcoming.Id, Assert.Single(result.Value.Items).Id);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task Includes_appointments_booked_by_an_earlier_owner()
    {
        var bookedBySeller = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        _vehicle.TransferTo(Guid.NewGuid());

        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(_vehicle.Id));

        var only = Assert.Single(result.Value.Items);
        Assert.Equal(bookedBySeller.Id, only.Id);
        Assert.NotEqual(_vehicle.CustomerId, only.CustomerId);
    }

    [Fact]
    public async Task Pages_through_the_appointments()
    {
        for (var day = 0; day < 5; day++)
        {
            _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot((24 * day) + 9, 1)));
        }

        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(_vehicle.Id, Page: 3, PageSize: 2));

        var only = Assert.Single(result.Value.Items);
        Assert.Equal(TestData.Slot(9, 1).Start, only.Start);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task A_vehicle_without_appointments_has_an_empty_list()
    {
        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(_vehicle.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Unknown_vehicle_is_not_found()
    {
        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal("vehicle.not_found", result.Error.Code);
    }

    [Theory]
    [InlineData("Booked")]
    [InlineData("1")]
    public async Task Unknown_status_is_a_validation_error(string status)
    {
        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(_vehicle.Id, Status: status));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal("appointment.unknown_status", result.Error.Code);
    }

    [Theory]
    [InlineData(0, 20, "paging.invalid_page")]
    [InlineData(1, 0, "paging.invalid_page_size")]
    [InlineData(1, Paging.MaxPageSize + 1, "paging.invalid_page_size")]
    public async Task Rejects_invalid_paging(int page, int pageSize, string code)
    {
        var result = await _list.HandleAsync(new ListVehicleAppointmentsQuery(_vehicle.Id, Page: page, PageSize: pageSize));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal(code, result.Error.Code);
    }
}
