using AutoFlow.Application.Appointments;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;

namespace AutoFlow.Application.Tests.Appointments;

public class AppointmentStatusHandlerTests
{
    /// <summary>Twenty minutes into <see cref="Slot"/>: the vehicle is late.</summary>
    private static readonly DateTimeOffset DuringSlot = TestData.Slot(9, 1).Start.AddMinutes(20);

    private readonly InMemoryStore _store = new();
    private readonly CheckInAppointmentHandler _checkIn;
    private readonly CancelAppointmentHandler _cancel;
    private readonly MarkAppointmentNoShowHandler _noShow;
    private readonly Customer _customer;
    private readonly Vehicle _vehicle;
    private readonly Bay _bay;

    public AppointmentStatusHandlerTests()
    {
        var time = new FixedTimeProvider(DuringSlot);
        _checkIn = new CheckInAppointmentHandler(_store, _store, _store, _store, time);
        _cancel = new CancelAppointmentHandler(_store, _store, time);
        _noShow = new MarkAppointmentNoShowHandler(_store, _store, time);
        _customer = _store.Seed(TestData.Customer());
        _vehicle = _store.Seed(TestData.Vehicle(_customer.Id, mileageKm: 50_000));
        _bay = _store.Seed(TestData.Bay());
    }

    private static AppointmentSlot Slot => TestData.Slot(9, 1);

    [Fact]
    public async Task CheckIn_opens_a_draft_work_order_from_the_purpose_and_links_it()
    {
        var appointment = SeedAppointment();

        var result = await _checkIn.HandleAsync(new CheckInAppointmentCommand(appointment.Id));

        Assert.True(result.IsSuccess);
        var (checkedIn, workOrder) = result.Value;
        Assert.Equal("CheckedIn", checkedIn.Status);
        Assert.Equal(DuringSlot, checkedIn.CheckedInAt);
        Assert.Equal(workOrder.Id, checkedIn.WorkOrderId);
        Assert.Equal("Draft", workOrder.Status);
        Assert.Equal(_vehicle.Id, workOrder.VehicleId);
        Assert.Equal(_customer.Id, workOrder.CustomerId);
        Assert.Equal("Annual service", workOrder.Complaint);
        Assert.Equal(50_000, workOrder.MileageKmAtIntake);
        Assert.Equal(DuringSlot, workOrder.CreatedAt);
        Assert.Equal(workOrder.Id, Assert.Single(_store.WorkOrders).Id);
        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task CheckIn_records_the_reading_and_takes_the_complaint_given_at_the_counter()
    {
        var appointment = SeedAppointment();

        var result = await _checkIn.HandleAsync(
            new CheckInAppointmentCommand(appointment.Id, 51_200, " Also a rattle at the rear "));

        Assert.Equal("Also a rattle at the rear", result.Value.WorkOrder.Complaint);
        Assert.Equal(51_200, result.Value.WorkOrder.MileageKmAtIntake);
        Assert.Equal(51_200, _vehicle.MileageKm);
    }

    [Fact]
    public async Task CheckIn_with_a_blank_complaint_uses_the_purpose()
    {
        var appointment = SeedAppointment();

        var result = await _checkIn.HandleAsync(new CheckInAppointmentCommand(appointment.Id, Complaint: "  "));

        Assert.Equal("Annual service", result.Value.WorkOrder.Complaint);
    }

    [Fact]
    public async Task CheckIn_opens_the_work_order_for_the_owner_after_a_sale()
    {
        var appointment = SeedAppointment();
        var buyer = _store.Seed(TestData.Customer("Jonas", "Becker"));
        _vehicle.TransferTo(buyer.Id);

        var result = await _checkIn.HandleAsync(new CheckInAppointmentCommand(appointment.Id));

        Assert.Equal(_customer.Id, result.Value.Appointment.CustomerId);
        Assert.Equal(buyer.Id, result.Value.WorkOrder.CustomerId);
    }

    [Fact]
    public async Task CheckIn_of_a_cancelled_appointment_throws_and_opens_no_work_order()
    {
        var appointment = SeedAppointment();
        appointment.Cancel("Customer is ill", FixedTimeProvider.Default);

        await Assert.ThrowsAsync<DomainException>(() =>
            _checkIn.HandleAsync(new CheckInAppointmentCommand(appointment.Id)));

        Assert.Empty(_store.WorkOrders);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task CheckIn_of_an_unknown_appointment_returns_not_found()
    {
        var result = await _checkIn.HandleAsync(new CheckInAppointmentCommand(Guid.NewGuid()));

        Assert.Equal("appointment.not_found", result.Error.Code);
        Assert.Empty(_store.WorkOrders);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Cancel_records_the_reason_and_frees_the_bay()
    {
        var appointment = SeedAppointment();

        var result = await _cancel.HandleAsync(new CancelAppointmentCommand(appointment.Id, " Customer is ill "));

        Assert.Equal("Cancelled", result.Value.Status);
        Assert.Equal("Customer is ill", result.Value.CancellationReason);
        Assert.Equal(DuringSlot, result.Value.CancelledAt);
        Assert.False(appointment.OccupiesBay);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Cancel_without_a_reason_throws_and_saves_nothing()
    {
        var appointment = SeedAppointment();

        await Assert.ThrowsAsync<DomainException>(() =>
            _cancel.HandleAsync(new CancelAppointmentCommand(appointment.Id, " ")));

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Cancel_of_an_unknown_appointment_returns_not_found()
    {
        var result = await _cancel.HandleAsync(new CancelAppointmentCommand(Guid.NewGuid(), "Customer is ill"));

        Assert.Equal("appointment.not_found", result.Error.Code);
    }

    [Fact]
    public async Task MarkNoShow_once_the_slot_has_begun_settles_the_appointment()
    {
        var appointment = SeedAppointment();

        var result = await _noShow.HandleAsync(new MarkAppointmentNoShowCommand(appointment.Id));

        Assert.Equal("NoShow", result.Value.Status);
        Assert.Equal(DuringSlot, result.Value.NoShowAt);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task MarkNoShow_before_the_slot_throws_and_saves_nothing()
    {
        var appointment = _store.Seed(TestData.Appointment(_bay, _vehicle, TestData.Slot(14, 1)));

        await Assert.ThrowsAsync<DomainException>(() =>
            _noShow.HandleAsync(new MarkAppointmentNoShowCommand(appointment.Id)));

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task MarkNoShow_of_a_checked_in_appointment_throws()
    {
        var appointment = SeedAppointment();
        var workOrder = _store.Seed(TestData.WorkOrder(_vehicle));
        appointment.CheckIn(workOrder.Id, DuringSlot);

        await Assert.ThrowsAsync<DomainException>(() =>
            _noShow.HandleAsync(new MarkAppointmentNoShowCommand(appointment.Id)));

        Assert.Equal(0, _store.SaveCount);
    }

    private Appointment SeedAppointment() => _store.Seed(TestData.Appointment(_bay, _vehicle, Slot));
}
