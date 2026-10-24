using AutoFlow.Application.Abstractions;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Tests;

[Collection(PostgresDefinition.Name)]
public class CustomerPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Added_customer_round_trips_with_all_fields()
    {
        var customer = TestData.Customer(phoneNumber: "+49 89 123456");

        await using (var db = postgres.CreateContext())
        {
            new CustomerRepository(db).Add(customer);
            await ((IUnitOfWork)db).SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var loaded = await new CustomerRepository(readDb).GetByIdAsync(customer.Id);

        Assert.NotNull(loaded);
        Assert.Equal(customer.Id, loaded.Id);
        Assert.Equal("Anna", loaded.FirstName);
        Assert.Equal("Schmidt", loaded.LastName);
        Assert.Equal(customer.Email, loaded.Email);
        Assert.Equal("anna.schmidt@example.com", loaded.Email.Value);
        Assert.Equal("+49 89 123456", loaded.PhoneNumber);
        Assert.Equal(TestData.Now, loaded.CreatedAt);
    }

    [Fact]
    public async Task Customer_without_phone_number_is_stored_with_null()
    {
        var customer = TestData.Customer(phoneNumber: null);

        await using (var db = postgres.CreateContext())
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var loaded = await readDb.Customers.SingleAsync(c => c.Id == customer.Id);

        Assert.Null(loaded.PhoneNumber);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_unknown_id()
    {
        await using var db = postgres.CreateContext();

        Assert.Null(await new CustomerRepository(db).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExistsAsync_reflects_stored_customers()
    {
        var customer = TestData.Customer();
        await using (var db = postgres.CreateContext())
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var repository = new CustomerRepository(readDb);

        Assert.True(await repository.ExistsAsync(customer.Id));
        Assert.False(await repository.ExistsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Changes_to_a_loaded_customer_are_saved_without_an_update_call()
    {
        var customer = TestData.Customer();
        await using (var db = postgres.CreateContext())
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateContext())
        {
            var loaded = await new CustomerRepository(db).GetByIdAsync(customer.Id);
            loaded!.Rename("Anna-Lena", "Schmidt-Weber");
            loaded.UpdateContactDetails("anna@werkstatt.de", "0171 1234567");
            await ((IUnitOfWork)db).SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.Customers.SingleAsync(c => c.Id == customer.Id);

        Assert.Equal("Anna-Lena", reloaded.FirstName);
        Assert.Equal("Schmidt-Weber", reloaded.LastName);
        Assert.Equal("anna@werkstatt.de", reloaded.Email.Value);
        Assert.Equal("0171 1234567", reloaded.PhoneNumber);
    }

    [Fact]
    public async Task Removed_customer_is_deleted_on_save()
    {
        var customer = TestData.Customer();
        await using (var db = postgres.CreateContext())
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateContext())
        {
            var repository = new CustomerRepository(db);
            repository.Remove((await repository.GetByIdAsync(customer.Id))!);
            await ((IUnitOfWork)db).SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        Assert.False(await readDb.Customers.AnyAsync(c => c.Id == customer.Id));
    }

    [Fact]
    public async Task Concurrent_edits_of_the_same_customer_are_rejected_for_the_second_writer()
    {
        var customer = TestData.Customer();
        await using (var db = postgres.CreateContext())
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var firstCopy = await first.Customers.SingleAsync(c => c.Id == customer.Id);
        var secondCopy = await second.Customers.SingleAsync(c => c.Id == customer.Id);

        firstCopy.Rename("Anna", "Meyer");
        await first.SaveChangesAsync();

        secondCopy.Rename("Anna", "Müller");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var readDb = postgres.CreateContext();
        Assert.Equal("Meyer", (await readDb.Customers.SingleAsync(c => c.Id == customer.Id)).LastName);
    }
}
