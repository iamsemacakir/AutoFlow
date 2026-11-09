using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Tests.Appointments;

public class AppointmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 7, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tomorrow9 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid BayId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static AppointmentSlot Hours(DateTimeOffset start, double hours) =>
        AppointmentSlot.StartingAt(start, TimeSpan.FromHours(hours));

    private static Appointment Book(
        Guid? bayId = null,
        Guid? vehicleId = null,
        AppointmentSlot? slot = null) =>
        Appointment.Book(
            bayId ?? BayId,
            vehicleId ?? VehicleId,
            CustomerId,
            slot ?? Hours(Tomorrow9, 2),
            "  Annual service ",
            Now);

    private static Appointment InStatus(AppointmentStatus status)
    {
        var appointment = Book();

        switch (status)
        {
            case AppointmentStatus.CheckedIn:
                appointment.CheckIn(Guid.NewGuid(), Tomorrow9);
                break;
            case AppointmentStatus.Cancelled:
                appointment.Cancel("Customer is ill", Now);
                break;
            case AppointmentStatus.NoShow:
                appointment.MarkNoShow(Tomorrow9.AddMinutes(30));
                break;
        }

        Assert.Equal(status, appointment.Status);
        return appointment;
    }

    [Fact]
    public void Book_creates_a_scheduled_appointment_that_occupies_its_bay()
    {
        var slot = Hours(Tomorrow9, 2);

        var appointment = Book(slot: slot);

        Assert.NotEqual(Guid.Empty, appointment.Id);
        Assert.Equal(BayId, appointment.BayId);
        Assert.Equal(VehicleId, appointment.VehicleId);
        Assert.Equal(CustomerId, appointment.CustomerId);
        Assert.Equal(slot, appointment.Slot);
        Assert.Equal("Annual service", appointment.Purpose);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Equal(Now, appointment.BookedAt);
        Assert.True(appointment.OccupiesBay);
        Assert.Null(appointment.RescheduledAt);
        Assert.Null(appointment.WorkOrderId);
        Assert.Null(appointment.CheckedInAt);
        Assert.Null(appointment.CancelledAt);
        Assert.Null(appointment.CancellationReason);
        Assert.Null(appointment.NoShowAt);
    }

    [Fact]
    public void Book_requires_bay_vehicle_and_customer()
    {
        var slot = Hours(Tomorrow9, 1);

        Assert.Throws<DomainException>(() => Appointment.Book(Guid.Empty, VehicleId, CustomerId, slot, "Service", Now));
        Assert.Throws<DomainException>(() => Appointment.Book(BayId, Guid.Empty, CustomerId, slot, "Service", Now));
        Assert.Throws<DomainException>(() => Appointment.Book(BayId, VehicleId, Guid.Empty, slot, "Service", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Book_requires_a_purpose(string purpose)
    {
        Assert.Throws<DomainException>(() =>
            Appointment.Book(BayId, VehicleId, CustomerId, Hours(Tomorrow9, 1), purpose, Now));
    }

    [Fact]
    public void Book_rejects_an_overlong_purpose()
    {
        Assert.Throws<DomainException>(() => Appointment.Book(
            BayId, VehicleId, CustomerId, Hours(Tomorrow9, 1), new string('x', Appointment.PurposeMaxLength + 1), Now));
    }

    [Fact]
    public void Book_accepts_the_current_quarter_hour_for_a_walk_in()
    {
        // Now is 08:07, so the 08:00 slot has not ended its first quarter hour yet.
        var appointment = Book(slot: Hours(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero), 1));

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Theory]
    [InlineData(7, 45)]
    [InlineData(6, 0)]
    public void Book_rejects_a_slot_that_starts_before_the_current_quarter_hour(int hour, int minute)
    {
        var slot = Hours(new DateTimeOffset(2026, 11, 9, hour, minute, 0, TimeSpan.Zero), 3);

        Assert.Throws<DomainException>(() => Book(slot: slot));
    }

    [Fact]
    public void UpdatePurpose_replaces_the_purpose_while_scheduled()
    {
        var appointment = Book();

        appointment.UpdatePurpose(" Brakes squeak ");

        Assert.Equal("Brakes squeak", appointment.Purpose);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void UpdatePurpose_is_rejected_once_the_appointment_is_settled(AppointmentStatus status)
    {
        var appointment = InStatus(status);

        Assert.Throws<DomainException>(() => appointment.UpdatePurpose("Something else"));
        Assert.Equal("Annual service", appointment.Purpose);
    }

    [Fact]
    public void Reschedule_moves_slot_and_bay_and_records_when()
    {
        var appointment = Book();
        var otherBay = Guid.NewGuid();
        var later = Hours(Tomorrow9.AddDays(1), 3);
        var rescheduledAt = Now.AddHours(1);

        appointment.Reschedule(otherBay, later, rescheduledAt);

        Assert.Equal(otherBay, appointment.BayId);
        Assert.Equal(later, appointment.Slot);
        Assert.Equal(rescheduledAt, appointment.RescheduledAt);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void Reschedule_may_change_only_the_bay_or_only_the_slot()
    {
        var appointment = Book();
        var otherBay = Guid.NewGuid();

        appointment.Reschedule(otherBay, appointment.Slot, Now);
        Assert.Equal(otherBay, appointment.BayId);

        var later = Hours(Tomorrow9.AddHours(4), 2);
        appointment.Reschedule(otherBay, later, Now);
        Assert.Equal(later, appointment.Slot);
    }

    [Fact]
    public void Reschedule_to_the_same_bay_and_slot_is_rejected()
    {
        var appointment = Book();

        Assert.Throws<DomainException>(() => appointment.Reschedule(BayId, Hours(Tomorrow9, 2), Now));
        Assert.Null(appointment.RescheduledAt);
    }

    [Fact]
    public void Reschedule_into_the_past_is_rejected_and_changes_nothing()
    {
        var appointment = Book();
        var original = appointment.Slot;

        Assert.Throws<DomainException>(() =>
            appointment.Reschedule(BayId, Hours(new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero), 1), Now));

        Assert.Equal(original, appointment.Slot);
        Assert.Null(appointment.RescheduledAt);
    }

    [Fact]
    public void Reschedule_requires_a_bay()
    {
        var appointment = Book();

        Assert.Throws<DomainException>(() => appointment.Reschedule(Guid.Empty, Hours(Tomorrow9, 1), Now));
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Reschedule_is_rejected_once_the_appointment_is_settled(AppointmentStatus status)
    {
        var appointment = InStatus(status);

        Assert.Throws<DomainException>(() => appointment.Reschedule(Guid.NewGuid(), Hours(Tomorrow9.AddDays(2), 1), Now));
        Assert.Equal(BayId, appointment.BayId);
    }

    [Fact]
    public void CheckIn_links_the_work_order_and_keeps_the_bay_occupied()
    {
        var appointment = Book();
        var workOrderId = Guid.NewGuid();
        var arrival = Tomorrow9.AddMinutes(-20);

        appointment.CheckIn(workOrderId, arrival);

        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Equal(workOrderId, appointment.WorkOrderId);
        Assert.Equal(arrival, appointment.CheckedInAt);
        Assert.True(appointment.OccupiesBay);
    }

    [Fact]
    public void CheckIn_requires_a_work_order()
    {
        var appointment = Book();

        Assert.Throws<DomainException>(() => appointment.CheckIn(Guid.Empty, Tomorrow9));
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void CheckIn_is_rejected_once_the_appointment_is_settled(AppointmentStatus status)
    {
        var appointment = InStatus(status);

        Assert.Throws<DomainException>(() => appointment.CheckIn(Guid.NewGuid(), Tomorrow9));
        Assert.Equal(status, appointment.Status);
    }

    [Fact]
    public void Cancel_records_the_reason_and_frees_the_bay()
    {
        var appointment = Book();

        appointment.Cancel("  Customer is ill ", Now);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("Customer is ill", appointment.CancellationReason);
        Assert.Equal(Now, appointment.CancelledAt);
        Assert.False(appointment.OccupiesBay);
    }

    [Fact]
    public void Cancel_requires_a_reason()
    {
        var appointment = Book();

        Assert.Throws<DomainException>(() => appointment.Cancel(" ", Now));
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Cancel_is_rejected_once_the_appointment_is_settled(AppointmentStatus status)
    {
        var appointment = InStatus(status);

        Assert.Throws<DomainException>(() => appointment.Cancel("Changed my mind", Now));
        Assert.Equal(status, appointment.Status);
    }

    [Fact]
    public void MarkNoShow_after_the_slot_began_frees_the_bay()
    {
        var appointment = Book();
        var markedAt = Tomorrow9.AddMinutes(45);

        appointment.MarkNoShow(markedAt);

        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Equal(markedAt, appointment.NoShowAt);
        Assert.False(appointment.OccupiesBay);
    }

    [Fact]
    public void MarkNoShow_at_the_start_of_the_slot_is_allowed()
    {
        var appointment = Book();

        appointment.MarkNoShow(Tomorrow9);

        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
    }

    [Fact]
    public void MarkNoShow_before_the_slot_is_rejected()
    {
        var appointment = Book();

        Assert.Throws<DomainException>(() => appointment.MarkNoShow(Tomorrow9.AddMinutes(-1)));
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void MarkNoShow_is_rejected_once_the_appointment_is_settled(AppointmentStatus status)
    {
        var appointment = InStatus(status);

        Assert.Throws<DomainException>(() => appointment.MarkNoShow(Tomorrow9.AddHours(1)));
        Assert.Equal(status, appointment.Status);
    }

    [Fact]
    public void ClashesInBayWith_is_true_for_overlapping_slots_in_the_same_bay()
    {
        var first = Book(vehicleId: Guid.NewGuid(), slot: Hours(Tomorrow9, 2));
        var second = Book(vehicleId: Guid.NewGuid(), slot: Hours(Tomorrow9.AddHours(1), 2));

        Assert.True(first.ClashesInBayWith(second));
        Assert.True(second.ClashesInBayWith(first));
    }

    [Fact]
    public void ClashesInBayWith_is_false_for_back_to_back_slots_or_another_bay()
    {
        var first = Book(vehicleId: Guid.NewGuid(), slot: Hours(Tomorrow9, 2));
        var backToBack = Book(vehicleId: Guid.NewGuid(), slot: Hours(Tomorrow9.AddHours(2), 1));
        var otherBay = Book(bayId: Guid.NewGuid(), vehicleId: Guid.NewGuid(), slot: Hours(Tomorrow9, 2));

        Assert.False(first.ClashesInBayWith(backToBack));
        Assert.False(first.ClashesInBayWith(otherBay));
    }

    [Fact]
    public void An_appointment_never_clashes_with_itself()
    {
        var appointment = Book();

        Assert.False(appointment.ClashesInBayWith(appointment));
        Assert.False(appointment.ClashesForVehicleWith(appointment));
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled, false)]
    [InlineData(AppointmentStatus.NoShow, false)]
    [InlineData(AppointmentStatus.CheckedIn, true)]
    public void Only_appointments_that_occupy_their_bay_clash(AppointmentStatus status, bool expected)
    {
        var settled = InStatus(status);
        var newcomer = Book(vehicleId: Guid.NewGuid(), slot: settled.Slot);

        Assert.Equal(expected, newcomer.ClashesInBayWith(settled));
        Assert.Equal(expected, settled.ClashesInBayWith(newcomer));
    }

    [Fact]
    public void ClashesForVehicleWith_is_true_for_the_same_vehicle_in_two_bays_at_once()
    {
        var first = Book(bayId: Guid.NewGuid(), slot: Hours(Tomorrow9, 2));
        var second = Book(bayId: Guid.NewGuid(), slot: Hours(Tomorrow9.AddMinutes(90), 1));

        Assert.True(first.ClashesForVehicleWith(second));
        Assert.False(first.ClashesInBayWith(second));
    }

    [Fact]
    public void ClashesForVehicleWith_is_false_for_another_vehicle_or_another_time()
    {
        var first = Book(bayId: Guid.NewGuid(), slot: Hours(Tomorrow9, 2));
        var otherVehicle = Book(bayId: Guid.NewGuid(), vehicleId: Guid.NewGuid(), slot: Hours(Tomorrow9, 2));
        var laterVisit = Book(bayId: Guid.NewGuid(), slot: Hours(Tomorrow9.AddDays(7), 2));

        Assert.False(first.ClashesForVehicleWith(otherVehicle));
        Assert.False(first.ClashesForVehicleWith(laterVisit));
    }
}
