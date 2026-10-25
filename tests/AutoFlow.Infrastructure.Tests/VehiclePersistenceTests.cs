using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Vehicles;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoFlow.Infrastructure.Tests;

[Collection(PostgresDefinition.Name)]
public class VehiclePersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Added_vehicle_round_trips_with_value_objects()
    {
        var owner = await SeedCustomerAsync();
        var vehicle = TestData.Vehicle(owner.Id);

        await using (var db = postgres.CreateContext())
        {
            new VehicleRepository(db).Add(vehicle);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var loaded = await new VehicleRepository(readDb).GetByIdAsync(vehicle.Id);

        Assert.NotNull(loaded);
        Assert.Equal(owner.Id, loaded.CustomerId);
        Assert.Equal(vehicle.Vin, loaded.Vin);
        Assert.Equal("M-AB 123", loaded.LicensePlate.Value);
        Assert.Equal("Volkswagen", loaded.Make);
        Assert.Equal("Golf", loaded.Model);
        Assert.Equal(2019, loaded.ModelYear);
        Assert.Equal(50_000, loaded.MileageKm);
        Assert.Equal(TestData.Now, loaded.RegisteredAt);
    }

    [Fact]
    public async Task ExistsWithVinAsync_matches_the_normalized_vin()
    {
        var owner = await SeedCustomerAsync();
        var vehicle = await SeedVehicleAsync(owner.Id);

        await using var db = postgres.CreateContext();
        var repository = new VehicleRepository(db);

        Assert.True(await repository.ExistsWithVinAsync(Vin.Create(vehicle.Vin.Value.ToLowerInvariant())));
        Assert.False(await repository.ExistsWithVinAsync(Vin.Create(TestData.UniqueVin())));
    }

    [Fact]
    public async Task ListByCustomerAsync_returns_only_that_customers_vehicles_oldest_first_untracked()
    {
        var owner = await SeedCustomerAsync();
        var otherOwner = await SeedCustomerAsync();
        var newer = await SeedVehicleAsync(owner.Id, TestData.Now.AddDays(1));
        var older = await SeedVehicleAsync(owner.Id, TestData.Now);
        await SeedVehicleAsync(otherOwner.Id);

        await using var db = postgres.CreateContext();
        var vehicles = await new VehicleRepository(db).ListByCustomerAsync(owner.Id);

        Assert.Equal(new[] { older.Id, newer.Id }, vehicles.Select(v => v.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ExistsForCustomerAsync_is_true_only_for_owners()
    {
        var owner = await SeedCustomerAsync();
        var customerWithoutVehicles = await SeedCustomerAsync();
        await SeedVehicleAsync(owner.Id);

        await using var db = postgres.CreateContext();
        var repository = new VehicleRepository(db);

        Assert.True(await repository.ExistsForCustomerAsync(owner.Id));
        Assert.False(await repository.ExistsForCustomerAsync(customerWithoutVehicles.Id));
    }

    [Fact]
    public async Task Recorded_mileage_and_transfer_are_saved_on_the_tracked_vehicle()
    {
        var owner = await SeedCustomerAsync();
        var buyer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleAsync(owner.Id);

        await using (var db = postgres.CreateContext())
        {
            var loaded = await new VehicleRepository(db).GetByIdAsync(vehicle.Id);
            loaded!.RecordMileage(61_250);
            loaded.TransferTo(buyer.Id);
            loaded.ChangeLicensePlate("HH-XY 9");
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.Vehicles.SingleAsync(v => v.Id == vehicle.Id);

        Assert.Equal(61_250, reloaded.MileageKm);
        Assert.Equal(buyer.Id, reloaded.CustomerId);
        Assert.Equal("HH-XY 9", reloaded.LicensePlate.Value);
    }

    [Fact]
    public async Task Unique_index_rejects_a_second_vehicle_with_the_same_vin()
    {
        var owner = await SeedCustomerAsync();
        var vin = TestData.UniqueVin();
        await SeedVehicleAsync(owner.Id, vin: vin);

        await using var db = postgres.CreateContext();
        db.Vehicles.Add(TestData.Vehicle(owner.Id, vin));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresError.SqlState);
        Assert.Equal("ux_vehicles_vin", postgresError.ConstraintName);
    }

    [Fact]
    public async Task Foreign_key_rejects_a_vehicle_of_an_unknown_customer()
    {
        await using var db = postgres.CreateContext();
        db.Vehicles.Add(TestData.Vehicle(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresError.SqlState);
    }

    [Fact]
    public async Task Customer_with_vehicles_cannot_be_deleted()
    {
        var owner = await SeedCustomerAsync();
        await SeedVehicleAsync(owner.Id);

        await using var db = postgres.CreateContext();
        db.Customers.Remove(await db.Customers.SingleAsync(c => c.Id == owner.Id));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresError.SqlState);
    }

    private async Task<Customer> SeedCustomerAsync()
    {
        var customer = TestData.Customer();
        await using var db = postgres.CreateContext();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private async Task<Vehicle> SeedVehicleAsync(
        Guid customerId,
        DateTimeOffset? registeredAt = null,
        string? vin = null)
    {
        var vehicle = TestData.Vehicle(customerId, vin, registeredAt);
        await using var db = postgres.CreateContext();
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle;
    }
}
