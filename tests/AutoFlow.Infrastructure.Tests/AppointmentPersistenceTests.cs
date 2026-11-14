using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Appointments;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Infrastructure.Persistence.Configurations;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoFlow.Infrastructure.Tests;

[Collection(PostgresDefinition.Name)]
public class AppointmentPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Booked_appointment_round_trips_with_its_slot_in_utc()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var slot = AppointmentSlot.Create(
            new DateTimeOffset(2026, 10, 22, 11, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 10, 22, 12, 30, 0, TimeSpan.FromHours(2)));
        var appointment = TestData.Appointment(bay.Id, vehicle.Id, customer.Id, slot);

        await using (var db = postgres.CreateContext())
        {
            new AppointmentRepository(db).Add(appointment);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var loaded = await new AppointmentRepository(readDb).GetByIdAsync(appointment.Id);

        Assert.NotNull(loaded);
        Assert.Equal(bay.Id, loaded.BayId);
        Assert.Equal(vehicle.Id, loaded.VehicleId);
        Assert.Equal(customer.Id, loaded.CustomerId);
        Assert.Equal(slot, loaded.Slot);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero), loaded.Slot.Start);
        Assert.Equal(TimeSpan.Zero, loaded.Slot.Start.Offset);
        Assert.Equal("Annual service", loaded.Purpose);
        Assert.Equal(AppointmentStatus.Scheduled, loaded.Status);
        Assert.Equal(TestData.Now, loaded.BookedAt);
        Assert.Null(loaded.RescheduledAt);
        Assert.Null(loaded.WorkOrderId);
    }

    [Fact]
    public async Task Reschedule_and_check_in_are_saved_on_the_tracked_appointment()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var otherBay = await SeedBayAsync();
        var appointment = await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(9, 1));
        var workOrder = TestData.WorkOrder(vehicle.Id, customer.Id);

        await using (var db = postgres.CreateContext())
        {
            var loaded = await new AppointmentRepository(db).GetByIdAsync(appointment.Id);
            loaded!.Reschedule(otherBay.Id, TestData.Slot(13, 2), TestData.Now.AddMinutes(5));
            db.WorkOrders.Add(workOrder);
            loaded.CheckIn(workOrder.Id, TestData.Now.AddMinutes(10));
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.Appointments.SingleAsync(a => a.Id == appointment.Id);

        Assert.Equal(otherBay.Id, reloaded.BayId);
        Assert.Equal(TestData.Slot(13, 2), reloaded.Slot);
        Assert.Equal<DateTimeOffset?>(TestData.Now.AddMinutes(5), reloaded.RescheduledAt);
        Assert.Equal(AppointmentStatus.CheckedIn, reloaded.Status);
        Assert.Equal(workOrder.Id, reloaded.WorkOrderId);
        Assert.Equal<DateTimeOffset?>(TestData.Now.AddMinutes(10), reloaded.CheckedInAt);
    }

    [Fact]
    public async Task Cancellation_is_saved_and_the_status_is_stored_by_name()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var appointment = TestData.Appointment(bay.Id, vehicle.Id, customer.Id, TestData.Slot(9, 1));
        appointment.Cancel("Customer is ill", TestData.Now.AddHours(1));
        await using (var seedDb = postgres.CreateContext())
        {
            seedDb.Appointments.Add(appointment);
            await seedDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        var reloaded = await db.Appointments.SingleAsync(a => a.Id == appointment.Id);
        var status = await db.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM appointments WHERE id = {appointment.Id}")
            .SingleAsync();

        Assert.Equal("Cancelled", status);
        Assert.Equal("Customer is ill", reloaded.CancellationReason);
        Assert.Equal<DateTimeOffset?>(TestData.Now.AddHours(1), reloaded.CancelledAt);
    }

    [Fact]
    public async Task Overlapping_booking_of_the_same_bay_violates_the_bay_exclusion_constraint()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var (_, otherCustomer, otherVehicle) = await SeedAsync();
        await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(9, 2));

        await using var db = postgres.CreateContext();
        db.Appointments.Add(TestData.Appointment(bay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(10.75, 1)));

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => ((IUnitOfWork)db).SaveChangesAsync());
        Assert.Equal(AppointmentConfiguration.BayExclusionConstraint, exception.ConstraintName);
    }

    [Fact]
    public async Task Overlapping_booking_of_the_same_vehicle_in_another_bay_violates_the_vehicle_exclusion_constraint()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var otherBay = await SeedBayAsync();
        await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(9, 2));

        await using var db = postgres.CreateContext();
        db.Appointments.Add(TestData.Appointment(otherBay.Id, vehicle.Id, customer.Id, TestData.Slot(8, 1.25)));

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => ((IUnitOfWork)db).SaveChangesAsync());
        Assert.Equal(AppointmentConfiguration.VehicleExclusionConstraint, exception.ConstraintName);
    }

    [Fact]
    public async Task Back_to_back_bookings_and_bookings_over_a_freed_slot_are_allowed()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var (_, otherCustomer, otherVehicle) = await SeedAsync();
        var cancelled = await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(13, 2));
        var noShow = await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(16, 1));
        await using (var settleDb = postgres.CreateContext())
        {
            (await settleDb.Appointments.SingleAsync(a => a.Id == cancelled.Id))
                .Cancel("Customer is ill", TestData.Now);
            (await settleDb.Appointments.SingleAsync(a => a.Id == noShow.Id))
                .MarkNoShow(TestData.Slot(16, 1).Start);
            await settleDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        db.Appointments.AddRange(
            TestData.Appointment(bay.Id, vehicle.Id, customer.Id, TestData.Slot(9, 2)),
            TestData.Appointment(bay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(11, 2)),
            TestData.Appointment(bay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(13, 2)),
            TestData.Appointment(bay.Id, vehicle.Id, customer.Id, TestData.Slot(16, 1)));

        await ((IUnitOfWork)db).SaveChangesAsync();
    }

    [Fact]
    public async Task ListClashCandidatesAsync_returns_overlapping_appointments_holding_the_bay_or_the_vehicle()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var (otherBay, otherCustomer, otherVehicle) = await SeedAsync();
        var inBay = await SeedAppointmentAsync(bay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(9, 1));
        var ofVehicle = await SeedAppointmentAsync(otherBay.Id, vehicle.Id, customer.Id, TestData.Slot(10.5, 1));
        await SeedAppointmentAsync(bay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(8, 1)); // ends at 09:00
        var thirdBay = await SeedBayAsync();
        await SeedAppointmentAsync(thirdBay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(10, 1)); // other bay and car
        var cancelled = TestData.Appointment(bay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(11, 1));
        cancelled.Cancel("Customer is ill", TestData.Now);
        await using (var seedDb = postgres.CreateContext())
        {
            seedDb.Appointments.Add(cancelled);
            await seedDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        var candidates = await new AppointmentRepository(db)
            .ListClashCandidatesAsync(bay.Id, vehicle.Id, TestData.Slot(9, 3));

        Assert.Collection(
            candidates,
            first => Assert.Equal(inBay.Id, first.Id),
            second => Assert.Equal(ofVehicle.Id, second.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ListOverlappingAsync_returns_appointments_of_the_period_except_cancelled_ones()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var (otherBay, otherCustomer, otherVehicle) = await SeedAsync();
        var fromYesterday = await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(-1, 2));
        var inOtherBay = await SeedAppointmentAsync(otherBay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(9, 1));
        var intoTomorrow = await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(23, 2));
        await SeedAppointmentAsync(otherBay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(-1, 1)); // ends at midnight
        await SeedAppointmentAsync(otherBay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(24, 1)); // starts at midnight
        var cancelled = TestData.Appointment(otherBay.Id, otherVehicle.Id, otherCustomer.Id, TestData.Slot(12, 1));
        cancelled.Cancel("Customer is ill", TestData.Now);
        await using (var seedDb = postgres.CreateContext())
        {
            seedDb.Appointments.Add(cancelled);
            await seedDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        var day = TestData.Slot(0, 24);
        var listed = await new AppointmentRepository(db).ListOverlappingAsync(day.Start, day.End);

        // Other tests book into the same days; only this test's bays are asserted.
        Assert.Equal(
            [fromYesterday.Id, inOtherBay.Id, intoTomorrow.Id],
            listed.Where(a => a.BayId == bay.Id || a.BayId == otherBay.Id).Select(a => a.Id).ToArray());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ExistsForCustomerAsync_is_true_only_for_customers_who_booked()
    {
        var (bay, customer, vehicle) = await SeedAsync();
        var (_, customerWithoutAppointments, _) = await SeedAsync();
        await SeedAppointmentAsync(bay.Id, vehicle.Id, customer.Id, TestData.Slot(9, 1));

        await using var db = postgres.CreateContext();
        var repository = new AppointmentRepository(db);

        Assert.True(await repository.ExistsForCustomerAsync(customer.Id));
        Assert.False(await repository.ExistsForCustomerAsync(customerWithoutAppointments.Id));
    }

    [Fact]
    public async Task Foreign_key_rejects_an_appointment_for_an_unknown_bay()
    {
        var (_, customer, vehicle) = await SeedAsync();

        await using var db = postgres.CreateContext();
        db.Appointments.Add(TestData.Appointment(Guid.NewGuid(), vehicle.Id, customer.Id, TestData.Slot(9, 1)));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresError.SqlState);
        Assert.Equal("fk_appointments_bays_bay_id", postgresError.ConstraintName);
    }

    private async Task<Appointment> SeedAppointmentAsync(
        Guid bayId,
        Guid vehicleId,
        Guid customerId,
        AppointmentSlot slot)
    {
        var appointment = TestData.Appointment(bayId, vehicleId, customerId, slot);
        await using var db = postgres.CreateContext();
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    private async Task<(Bay Bay, Customer Customer, Vehicle Vehicle)> SeedAsync()
    {
        var bay = TestData.Bay();
        var customer = TestData.Customer();
        var vehicle = TestData.Vehicle(customer.Id);
        await using var db = postgres.CreateContext();
        db.Bays.Add(bay);
        db.Customers.Add(customer);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return (bay, customer, vehicle);
    }

    private async Task<Bay> SeedBayAsync()
    {
        var bay = TestData.Bay();
        await using var db = postgres.CreateContext();
        db.Bays.Add(bay);
        await db.SaveChangesAsync();
        return bay;
    }
}
