using AutoFlow.Application;
using AutoFlow.Application.Appointments;
using AutoFlow.Application.Bays;
using AutoFlow.Application.Common;
using AutoFlow.Application.Customers;
using AutoFlow.Application.Vehicles;
using AutoFlow.Application.WorkOrders;
using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>
/// Runs the real use-case handlers against PostgreSQL through the same DI
/// registrations the API will use, one scope per simulated request.
/// </summary>
[Collection(PostgresDefinition.Name)]
public sealed class UseCaseIntegrationTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public UseCaseIntegrationTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        _provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now))
            .AddApplication()
            .AddInfrastructure(postgres.ConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task Customer_and_vehicle_registered_in_separate_requests_are_linked()
    {
        var customer = await InScope<RegisterCustomerHandler, CustomerDetails>(handler =>
            handler.HandleAsync(new RegisterCustomerCommand("Jonas", "Becker", "jonas@example.com", null)));

        var vin = TestData.UniqueVin();
        var vehicle = await InScope<RegisterVehicleHandler, VehicleDetails>(handler =>
            handler.HandleAsync(new RegisterVehicleCommand(customer.Id, vin, "B-JB 42", "BMW", "320d", 2021, 30_000)));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoFlowDbContext>();
        var stored = await db.Vehicles.SingleAsync(v => v.Id == vehicle.Id);

        Assert.Equal(customer.Id, stored.CustomerId);
        Assert.Equal(vin, stored.Vin.Value);
        Assert.Equal(TestData.Now, stored.RegisteredAt);
    }

    [Fact]
    public async Task Registering_a_known_vin_again_returns_a_conflict()
    {
        var customer = await InScope<RegisterCustomerHandler, CustomerDetails>(handler =>
            handler.HandleAsync(new RegisterCustomerCommand("Mia", "Wagner", "mia@example.com", null)));
        var command = new RegisterVehicleCommand(customer.Id, TestData.UniqueVin(), "K-MW 7", "Opel", "Corsa", 2018, 80_000);
        await InScope<RegisterVehicleHandler, VehicleDetails>(handler => handler.HandleAsync(command));

        using var scope = _provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RegisterVehicleHandler>().HandleAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.duplicate_vin", result.Error.Code);
    }

    [Fact]
    public async Task Opening_a_work_order_stores_it_and_the_intake_reading_in_one_commit()
    {
        var customer = await InScope<RegisterCustomerHandler, CustomerDetails>(handler =>
            handler.HandleAsync(new RegisterCustomerCommand("Lena", "Hoffmann", "lena@example.com", null)));
        var vehicle = await InScope<RegisterVehicleHandler, VehicleDetails>(handler =>
            handler.HandleAsync(new RegisterVehicleCommand(customer.Id, TestData.UniqueVin(), "S-LH 5", "Audi", "A4", 2020, 40_000)));

        var opened = await InScope<OpenWorkOrderHandler, WorkOrderDetails>(handler =>
            handler.HandleAsync(new OpenWorkOrderCommand(vehicle.Id, "Annual service", 45_300)));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoFlowDbContext>();
        var stored = await db.WorkOrders.SingleAsync(w => w.Id == opened.Id);
        var storedVehicle = await db.Vehicles.SingleAsync(v => v.Id == vehicle.Id);

        Assert.Equal(customer.Id, stored.CustomerId);
        Assert.Equal(45_300, stored.MileageKmAtIntake);
        Assert.Equal(TestData.Now, stored.CreatedAt);
        Assert.Equal(45_300, storedVehicle.MileageKm);
    }

    [Fact]
    public async Task Lines_added_and_removed_in_separate_requests_are_stored_with_the_work_order()
    {
        var customer = await InScope<RegisterCustomerHandler, CustomerDetails>(handler =>
            handler.HandleAsync(new RegisterCustomerCommand("Paul", "Richter", "paul@example.com", null)));
        var vehicle = await InScope<RegisterVehicleHandler, VehicleDetails>(handler =>
            handler.HandleAsync(new RegisterVehicleCommand(customer.Id, TestData.UniqueVin(), "H-PR 9", "Skoda", "Octavia", 2017, 120_000)));
        var opened = await InScope<OpenWorkOrderHandler, WorkOrderDetails>(handler =>
            handler.HandleAsync(new OpenWorkOrderCommand(vehicle.Id, "Brakes grinding")));

        var withLabour = await InScope<AddLabourLineHandler, WorkOrderDetails>(handler =>
            handler.HandleAsync(new AddLabourLineCommand(opened.Id, "Diagnosis", 0.5m, 95m)));
        await InScope<AddPartLineHandler, WorkOrderDetails>(handler =>
            handler.HandleAsync(new AddPartLineCommand(opened.Id, "bp-12", "Brake pads, front", 1m, 64m)));
        await InScope<RemoveWorkOrderLineHandler, WorkOrderDetails>(handler =>
            handler.HandleAsync(new RemoveWorkOrderLineCommand(opened.Id, withLabour.Lines[0].Id)));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoFlowDbContext>();
        var stored = await db.WorkOrders.SingleAsync(w => w.Id == opened.Id);

        var line = Assert.Single(stored.Lines);
        Assert.Equal(2, line.Number);
        Assert.Equal("BP-12", line.PartNumber);
        Assert.Equal(64m, stored.Total);
    }

    [Fact]
    public async Task Booking_and_rescheduling_check_clashes_against_stored_appointments()
    {
        var customer = await InScope<RegisterCustomerHandler, CustomerDetails>(handler =>
            handler.HandleAsync(new RegisterCustomerCommand("Eva", "Krause", "eva@example.com", null)));
        var vehicle = await InScope<RegisterVehicleHandler, VehicleDetails>(handler =>
            handler.HandleAsync(new RegisterVehicleCommand(customer.Id, TestData.UniqueVin(), "D-EK 3", "Ford", "Focus", 2016, 90_000)));
        var otherVehicle = await InScope<RegisterVehicleHandler, VehicleDetails>(handler =>
            handler.HandleAsync(new RegisterVehicleCommand(customer.Id, TestData.UniqueVin(), "D-EK 4", "Ford", "Fiesta", 2018, 60_000)));
        var bay = await InScope<CreateBayHandler, BayDetails>(handler =>
            handler.HandleAsync(new CreateBayCommand(TestData.UniqueBayName())));
        var morning = TestData.Slot(9, 2);
        var booked = await InScope<BookAppointmentHandler, AppointmentDetails>(handler =>
            handler.HandleAsync(new BookAppointmentCommand(vehicle.Id, bay.Id, morning.Start, morning.End, "Annual service")));

        using (var scope = _provider.CreateScope())
        {
            var clash = TestData.Slot(10, 1);
            var refused = await scope.ServiceProvider.GetRequiredService<BookAppointmentHandler>().HandleAsync(
                new BookAppointmentCommand(otherVehicle.Id, bay.Id, clash.Start, clash.End, "Oil change"));

            Assert.Equal("appointment.bay_already_booked", refused.Error.Code);
        }

        var afternoon = TestData.Slot(14, 2);
        await InScope<RescheduleAppointmentHandler, AppointmentDetails>(handler =>
            handler.HandleAsync(new RescheduleAppointmentCommand(booked.Id, bay.Id, afternoon.Start, afternoon.End)));
        var second = await InScope<BookAppointmentHandler, AppointmentDetails>(handler =>
            handler.HandleAsync(new BookAppointmentCommand(otherVehicle.Id, bay.Id, morning.Start, morning.End, "Oil change")));

        using var readScope = _provider.CreateScope();
        var db = readScope.ServiceProvider.GetRequiredService<AutoFlowDbContext>();
        var stored = await db.Appointments.SingleAsync(a => a.Id == booked.Id);

        Assert.Equal(afternoon, stored.Slot);
        Assert.Equal(customer.Id, stored.CustomerId);
        Assert.Equal<DateTimeOffset?>(TestData.Now, stored.RescheduledAt);
        Assert.Equal(morning.Start, second.Start);
    }

    public void Dispose() => _provider.Dispose();

    private async Task<TOut> InScope<THandler, TOut>(Func<THandler, Task<Result<TOut>>> call)
        where THandler : notnull
    {
        using var scope = _provider.CreateScope();
        var result = await call(scope.ServiceProvider.GetRequiredService<THandler>());
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
