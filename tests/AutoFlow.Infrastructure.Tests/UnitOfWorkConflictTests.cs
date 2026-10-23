using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>
/// The unit of work turns lost races into the application's conflict
/// exceptions and leaves every other database error untouched.
/// </summary>
[Collection(PostgresDefinition.Name)]
public class UnitOfWorkConflictTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SaveChangesAsync_translates_a_duplicate_vin_into_a_unique_constraint_violation()
    {
        var owner = await SeedCustomerAsync();
        var vin = TestData.UniqueVin();
        await using (var db = postgres.CreateContext())
        {
            db.Vehicles.Add(TestData.Vehicle(owner.Id, vin));
            await db.SaveChangesAsync();
        }

        await using var racingDb = postgres.CreateContext();
        racingDb.Vehicles.Add(TestData.Vehicle(owner.Id, vin));

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => ((IUnitOfWork)racingDb).SaveChangesAsync());

        Assert.Equal("ux_vehicles_vin", exception.ConstraintName);
        Assert.IsType<DbUpdateException>(exception.InnerException);
    }

    [Fact]
    public async Task SaveChangesAsync_translates_a_stale_update_into_a_concurrency_conflict()
    {
        var customer = await SeedCustomerAsync();

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var firstCopy = await first.Customers.SingleAsync(c => c.Id == customer.Id);
        var secondCopy = await second.Customers.SingleAsync(c => c.Id == customer.Id);

        firstCopy.Rename("Anna", "Meyer");
        await ((IUnitOfWork)first).SaveChangesAsync();

        secondCopy.Rename("Anna", "Müller");
        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => ((IUnitOfWork)second).SaveChangesAsync());

        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
        await using var readDb = postgres.CreateContext();
        Assert.Equal("Meyer", (await readDb.Customers.SingleAsync(c => c.Id == customer.Id)).LastName);
    }

    [Fact]
    public async Task SaveChangesAsync_leaves_other_constraint_violations_untranslated()
    {
        await using var db = postgres.CreateContext();
        db.Vehicles.Add(TestData.Vehicle(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => ((IUnitOfWork)db).SaveChangesAsync());

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
}
