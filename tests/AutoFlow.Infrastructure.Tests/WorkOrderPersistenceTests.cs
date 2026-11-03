using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Domain.WorkOrders;
using AutoFlow.Infrastructure.Persistence.Configurations;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoFlow.Infrastructure.Tests;

[Collection(PostgresDefinition.Name)]
public class WorkOrderPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Added_work_order_round_trips_with_its_lines_in_order()
    {
        var (customer, vehicle) = await SeedVehicleAsync();
        var workOrder = TestData.WorkOrder(vehicle.Id, customer.Id);
        // Enough lines that an unordered read would very likely shuffle them.
        for (var i = 1; i <= 12; i++)
        {
            workOrder.AddPart($"p-{i}", $"Part {i}", 1.5m, 10.05m * i);
        }

        workOrder.AddLabour("Fit parts", 2.25m, 96m);

        await using (var db = postgres.CreateContext())
        {
            new WorkOrderRepository(db).Add(workOrder);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var loaded = await new WorkOrderRepository(readDb).GetByIdAsync(workOrder.Id);

        Assert.NotNull(loaded);
        Assert.Equal(vehicle.Id, loaded.VehicleId);
        Assert.Equal(customer.Id, loaded.CustomerId);
        Assert.Equal("Squeaking brakes at the front", loaded.Complaint);
        Assert.Equal(62_500, loaded.MileageKmAtIntake);
        Assert.Equal(WorkOrderStatus.Draft, loaded.Status);
        Assert.Equal(TestData.Now, loaded.CreatedAt);
        Assert.Equal(Enumerable.Range(1, 13), loaded.Lines.Select(l => l.Number));
        Assert.Equal(workOrder.Lines.Select(l => l.Id), loaded.Lines.Select(l => l.Id));

        var part = loaded.Lines[2];
        Assert.Equal(WorkOrderLineKind.Part, part.Kind);
        Assert.Equal("P-3", part.PartNumber);
        Assert.Equal("Part 3", part.Description);
        Assert.Equal(1.5m, part.Quantity);
        Assert.Equal(30.15m, part.UnitPrice);

        var labour = loaded.Lines[^1];
        Assert.Equal(WorkOrderLineKind.Labour, labour.Kind);
        Assert.Null(labour.PartNumber);
        Assert.Equal(216m, labour.Total);
        Assert.Equal(workOrder.Total, loaded.Total);
    }

    [Fact]
    public async Task Status_changes_and_their_timestamps_are_saved_on_the_tracked_work_order()
    {
        var workOrder = await SeedWorkOrderAsync(w => w.AddLabour("Replace front brake pads", 1.5m, 95m));

        await using (var db = postgres.CreateContext())
        {
            var loaded = await new WorkOrderRepository(db).GetByIdAsync(workOrder.Id);
            loaded!.Approve(TestData.Now.AddHours(1));
            loaded.Start(TestData.Now.AddHours(2));
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.WorkOrders.SingleAsync(w => w.Id == workOrder.Id);

        Assert.Equal(WorkOrderStatus.InProgress, reloaded.Status);
        Assert.Equal<DateTimeOffset?>(TestData.Now.AddHours(1), reloaded.ApprovedAt);
        Assert.Equal<decimal?>(142.50m, reloaded.ApprovedTotal);
        Assert.Equal<DateTimeOffset?>(TestData.Now.AddHours(2), reloaded.StartedAt);
        Assert.Null(reloaded.CancelledAt);
    }

    [Fact]
    public async Task Status_and_line_kind_are_stored_by_name()
    {
        var workOrder = await SeedWorkOrderAsync(w =>
        {
            w.AddPart(null, "Brake fluid", 1m, 14m);
            w.Cancel("Customer declined the estimate", TestData.Now);
        });

        await using var db = postgres.CreateContext();
        var status = await db.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM work_orders WHERE id = {workOrder.Id}")
            .SingleAsync();
        var kind = await db.Database
            .SqlQuery<string>($"SELECT kind AS \"Value\" FROM work_order_lines WHERE work_order_id = {workOrder.Id}")
            .SingleAsync();

        Assert.Equal("Cancelled", status);
        Assert.Equal("Part", kind);
    }

    [Fact]
    public async Task Added_and_removed_lines_are_saved_and_bump_the_work_order_version()
    {
        var workOrder = await SeedWorkOrderAsync(w => w.AddLabour("Diagnosis", 0.5m, 95m));
        var removedLineId = workOrder.Lines[0].Id;
        var versionBefore = await ReadVersionAsync(workOrder.Id);

        await using (var db = postgres.CreateContext())
        {
            var loaded = await new WorkOrderRepository(db).GetByIdAsync(workOrder.Id);
            loaded!.AddPart("BP-12", "Brake pads, front", 1m, 64m);
            loaded.RemoveLine(removedLineId);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.WorkOrders.SingleAsync(w => w.Id == workOrder.Id);

        var line = Assert.Single(reloaded.Lines);
        Assert.Equal("Brake pads, front", line.Description);
        Assert.Equal(2, line.Number);
        Assert.NotEqual(versionBefore, await ReadVersionAsync(workOrder.Id));
    }

    [Fact]
    public async Task Concurrent_line_edits_of_one_work_order_conflict()
    {
        var workOrder = await SeedWorkOrderAsync(w => w.AddLabour("Diagnosis", 0.5m, 95m));

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var firstCopy = await new WorkOrderRepository(first).GetByIdAsync(workOrder.Id);
        var secondCopy = await new WorkOrderRepository(second).GetByIdAsync(workOrder.Id);

        firstCopy!.AddPart(null, "Bulb H7", 1m, 6.50m);
        await ((IUnitOfWork)first).SaveChangesAsync();

        // Touches only the line row, which the first request did not change.
        secondCopy!.RemoveLine(secondCopy.Lines[0].Id);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ((IUnitOfWork)second).SaveChangesAsync());

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.WorkOrders.SingleAsync(w => w.Id == workOrder.Id);
        Assert.Collection(
            reloaded.Lines,
            line => Assert.Equal("Diagnosis", line.Description),
            line => Assert.Equal("Bulb H7", line.Description));
    }

    [Fact]
    public async Task Saving_without_line_changes_leaves_the_version_unchanged()
    {
        var workOrder = await SeedWorkOrderAsync(w => w.AddLabour("Diagnosis", 0.5m, 95m));
        var versionBefore = await ReadVersionAsync(workOrder.Id);

        await using (var db = postgres.CreateContext())
        {
            await new WorkOrderRepository(db).GetByIdAsync(workOrder.Id);
            await db.SaveChangesAsync();
        }

        Assert.Equal(versionBefore, await ReadVersionAsync(workOrder.Id));
    }

    [Fact]
    public async Task Foreign_key_rejects_a_work_order_for_an_unknown_vehicle()
    {
        var customer = await SeedCustomerAsync();

        await using var db = postgres.CreateContext();
        db.WorkOrders.Add(TestData.WorkOrder(Guid.NewGuid(), customer.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresError.SqlState);
        Assert.Equal("fk_work_orders_vehicles_vehicle_id", postgresError.ConstraintName);
    }

    [Fact]
    public async Task Customer_with_work_orders_cannot_be_deleted_even_after_selling_the_vehicle()
    {
        var workOrder = await SeedWorkOrderAsync(w => w.AddLabour("Diagnosis", 0.5m, 95m));
        var buyer = await SeedCustomerAsync();
        await using (var db = postgres.CreateContext())
        {
            var vehicle = await db.Vehicles.SingleAsync(v => v.Id == workOrder.VehicleId);
            vehicle.TransferTo(buyer.Id);
            await db.SaveChangesAsync();
        }

        await using var deleteDb = postgres.CreateContext();
        deleteDb.Customers.Remove(await deleteDb.Customers.SingleAsync(c => c.Id == workOrder.CustomerId));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => deleteDb.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("fk_work_orders_customers_customer_id", postgresError.ConstraintName);
    }

    private async Task<uint> ReadVersionAsync(Guid workOrderId)
    {
        await using var db = postgres.CreateContext();
        return await db.WorkOrders
            .Where(w => w.Id == workOrderId)
            .Select(w => EF.Property<uint>(w, RowVersionExtensions.PropertyName))
            .SingleAsync();
    }

    private async Task<WorkOrder> SeedWorkOrderAsync(Action<WorkOrder> prepare)
    {
        var (customer, vehicle) = await SeedVehicleAsync();
        var workOrder = TestData.WorkOrder(vehicle.Id, customer.Id);
        prepare(workOrder);

        await using var db = postgres.CreateContext();
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        return workOrder;
    }

    private async Task<(Customer Customer, Vehicle Vehicle)> SeedVehicleAsync()
    {
        var customer = await SeedCustomerAsync();
        var vehicle = TestData.Vehicle(customer.Id);
        await using var db = postgres.CreateContext();
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return (customer, vehicle);
    }

    private async Task<Customer> SeedCustomerAsync()
    {
        var customer = TestData.Customer();
        await using var db = postgres.CreateContext();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }
}
