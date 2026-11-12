using AutoFlow.Application.Appointments;
using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Tests.Appointments;

public class BookingHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly BookAppointmentHandler _book;
    private readonly RescheduleAppointmentHandler _reschedule;
    private readonly GetAppointmentHandler _get;
    private readonly Customer _customer;
    private readonly Vehicle _vehicle;
    private readonly Bay _bay;

    public BookingHandlerTests()
    {
        var time = new FixedTimeProvider();
        _book = new BookAppointmentHandler(_store, _store, _store, _store, time);
        _reschedule = new RescheduleAppointmentHandler(_store, _store, _store, time);
        _get = new GetAppointmentHandler(_store);
        _customer = _store.Seed(TestData.Customer());
        _vehicle = _store.Seed(TestData.Vehicle(_customer.Id));
        _bay = _store.Seed(TestData.Bay());
    }

    [Fact]
    public async Task Book_reserves_the_bay_for_the_vehicles_current_owner()
    {
        var slot = TestData.Slot(9, 1.5);

        var result = await _book.HandleAsync(Command(slot, purpose: "  Annual service "));

        Assert.True(result.IsSuccess);
        var details = result.Value;
        Assert.Equal(_bay.Id, details.BayId);
        Assert.Equal(_vehicle.Id, details.VehicleId);
        Assert.Equal(_customer.Id, details.CustomerId);
        Assert.Equal(slot.Start, details.Start);
        Assert.Equal(slot.End, details.End);
        Assert.Equal("Annual service", details.Purpose);
        Assert.Equal("Scheduled", details.Status);
        Assert.Equal(FixedTimeProvider.Default, details.BookedAt);
        Assert.Null(details.WorkOrderId);
        Assert.Equal(details.Id, Assert.Single(_store.Appointments).Id);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Book_stores_a_slot_given_with_an_offset_in_utc()
    {
        var start = new DateTimeOffset(2026, 10, 21, 11, 0, 0, TimeSpan.FromHours(2));

        var result = await _book.HandleAsync(
            new BookAppointmentCommand(_vehicle.Id, _bay.Id, start, start.AddHours(1), "Oil change"));

        Assert.Equal(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero), result.Value.Start);
        Assert.Equal(TimeSpan.Zero, result.Value.Start.Offset);
    }

    [Fact]
    public async Task Book_for_an_unknown_vehicle_returns_not_found()
    {
        var result = await _book.HandleAsync(
            new BookAppointmentCommand(Guid.NewGuid(), _bay.Id, TestData.Slot(9, 1).Start, TestData.Slot(9, 1).End, "Oil change"));

        Assert.Equal("vehicle.not_found", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Book_into_an_unknown_bay_returns_not_found()
    {
        var slot = TestData.Slot(9, 1);

        var result = await _book.HandleAsync(
            new BookAppointmentCommand(_vehicle.Id, Guid.NewGuid(), slot.Start, slot.End, "Oil change"));

        Assert.Equal("bay.not_found", result.Error.Code);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Book_into_a_bay_out_of_service_returns_a_conflict()
    {
        _bay.TakeOutOfService();

        var result = await _book.HandleAsync(Command(TestData.Slot(9, 1)));

        Assert.Equal("appointment.bay_out_of_service", result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Empty(_store.Appointments);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Book_over_another_vehicle_in_the_bay_returns_a_conflict_naming_the_clash()
    {
        var otherVehicle = _store.Seed(TestData.Vehicle(_customer.Id, vin: "WVWZZZ1JZXW000002"));
        var existing = _store.Seed(TestData.Appointment(_bay, otherVehicle, TestData.Slot(9, 2)));

        var result = await _book.HandleAsync(Command(TestData.Slot(10.75, 1)));

        Assert.Equal("appointment.bay_already_booked", result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Contains(existing.Id.ToString(), result.Error.Message, StringComparison.Ordinal);
        Assert.Single(_store.Appointments);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Book_of_a_vehicle_already_booked_into_another_bay_returns_a_conflict()
    {
        var otherBay = _store.Seed(TestData.Bay("Lift 2"));
        _store.Seed(TestData.Appointment(otherBay, _vehicle, TestData.Slot(8, 1.25)));

        var result = await _book.HandleAsync(Command(TestData.Slot(9, 1)));

        Assert.Equal("appointment.vehicle_already_booked", result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Book_back_to_back_and_over_a_cancelled_slot_succeeds()
    {
        var otherVehicle = _store.Seed(TestData.Vehicle(_customer.Id, vin: "WVWZZZ1JZXW000002"));
        _store.Seed(TestData.Appointment(_bay, otherVehicle, TestData.Slot(8, 1)));
        var cancelled = _store.Seed(TestData.Appointment(_bay, otherVehicle, TestData.Slot(9, 1)));
        cancelled.Cancel("Customer is ill", FixedTimeProvider.Default);

        var result = await _book.HandleAsync(Command(TestData.Slot(9, 1)));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _store.Appointments.Count);
    }

    [Fact]
    public async Task Book_in_the_past_throws_and_saves_nothing()
    {
        var past = AppointmentSlot.StartingAt(FixedTimeProvider.Default.AddHours(-1), TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<DomainException>(() => _book.HandleAsync(Command(past)));

        Assert.Empty(_store.Appointments);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Book_off_the_quarter_hour_grid_throws()
    {
        var start = TestData.Slot(9, 1).Start.AddMinutes(5);

        await Assert.ThrowsAsync<DomainException>(() => _book.HandleAsync(
            new BookAppointmentCommand(_vehicle.Id, _bay.Id, start, start.AddHours(1), "Oil change")));

        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Reschedule_moves_the_appointment_to_another_bay_and_slot()
    {
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        var otherBay = _store.Seed(TestData.Bay("Lift 2"));
        var slot = TestData.Slot(13, 2);

        var result = await _reschedule.HandleAsync(
            new RescheduleAppointmentCommand(appointment.Id, otherBay.Id, slot.Start, slot.End));

        Assert.True(result.IsSuccess);
        Assert.Equal(otherBay.Id, result.Value.BayId);
        Assert.Equal(slot.Start, result.Value.Start);
        Assert.Equal(slot.End, result.Value.End);
        Assert.Equal(FixedTimeProvider.Default, result.Value.RescheduledAt);
        Assert.Equal(slot, appointment.Slot);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Reschedule_within_its_own_current_slot_is_not_a_clash()
    {
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 2)));
        var longer = TestData.Slot(9, 3);

        var result = await _reschedule.HandleAsync(
            new RescheduleAppointmentCommand(appointment.Id, _bay.Id, longer.Start, longer.End));

        Assert.True(result.IsSuccess);
        Assert.Equal(longer.End, result.Value.End);
    }

    [Fact]
    public async Task Reschedule_onto_another_booking_in_the_bay_returns_a_conflict_and_saves_nothing()
    {
        var otherVehicle = _store.Seed(TestData.Vehicle(_customer.Id, vin: "WVWZZZ1JZXW000002"));
        _store.Seed(TestData.Appointment(_bay, otherVehicle, TestData.Slot(13, 1)));
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        var slot = TestData.Slot(12.5, 1);

        var result = await _reschedule.HandleAsync(
            new RescheduleAppointmentCommand(appointment.Id, _bay.Id, slot.Start, slot.End));

        Assert.Equal("appointment.bay_already_booked", result.Error.Code);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Reschedule_in_a_bay_that_went_out_of_service_returns_a_conflict()
    {
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        _bay.TakeOutOfService();
        var slot = TestData.Slot(14, 1);

        var result = await _reschedule.HandleAsync(
            new RescheduleAppointmentCommand(appointment.Id, _bay.Id, slot.Start, slot.End));

        Assert.Equal("appointment.bay_out_of_service", result.Error.Code);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Reschedule_of_a_cancelled_appointment_throws_and_saves_nothing()
    {
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        appointment.Cancel("Customer is ill", FixedTimeProvider.Default);
        var slot = TestData.Slot(14, 1);

        await Assert.ThrowsAsync<DomainException>(() => _reschedule.HandleAsync(
            new RescheduleAppointmentCommand(appointment.Id, _bay.Id, slot.Start, slot.End)));

        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Reschedule_of_an_unknown_appointment_or_into_an_unknown_bay_returns_not_found()
    {
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));
        var slot = TestData.Slot(14, 1);

        var unknownAppointment = await _reschedule.HandleAsync(
            new RescheduleAppointmentCommand(Guid.NewGuid(), _bay.Id, slot.Start, slot.End));
        var unknownBay = await _reschedule.HandleAsync(
            new RescheduleAppointmentCommand(appointment.Id, Guid.NewGuid(), slot.Start, slot.End));

        Assert.Equal("appointment.not_found", unknownAppointment.Error.Code);
        Assert.Equal("bay.not_found", unknownBay.Error.Code);
        Assert.Equal(TestData.Slot(9, 1), appointment.Slot);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Get_returns_the_appointment_or_not_found()
    {
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(9, 1)));

        var found = await _get.HandleAsync(new GetAppointmentQuery(appointment.Id));
        var missing = await _get.HandleAsync(new GetAppointmentQuery(Guid.NewGuid()));

        Assert.Equal(appointment.Id, found.Value.Id);
        Assert.Equal("Scheduled", found.Value.Status);
        Assert.Equal("appointment.not_found", missing.Error.Code);
        Assert.Equal(ErrorKind.NotFound, missing.Error.Kind);
    }

    private BookAppointmentCommand Command(AppointmentSlot slot, string purpose = "Oil change") =>
        new(_vehicle.Id, _bay.Id, slot.Start, slot.End, purpose);
}
