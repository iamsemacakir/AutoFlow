using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using AutoFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>
/// The versions behind the API's <c>ETag</c> / <c>If-Match</c> (ADR 0024):
/// read from <c>xmin</c>, and enforced at commit time when a request expects one.
/// </summary>
[Collection(PostgresDefinition.Name)]
public class RowVersionsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetAsync_reads_a_version_that_changes_with_every_committed_change()
    {
        var customer = await SeedCustomerAsync();
        await using var db = postgres.CreateContext();
        var versions = new RowVersions(db);

        var before = await versions.GetAsync<Customer>(customer.Id);
        await RenameAsync(customer.Id, "Meyer");
        var after = await versions.GetAsync<Customer>(customer.Id);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before, after);
        Assert.Null(await versions.GetAsync<Customer>(Guid.NewGuid()));
    }

    [Fact]
    public async Task An_expected_version_that_was_overtaken_before_the_load_fails_the_commit()
    {
        var customer = await SeedCustomerAsync();
        await using var db = postgres.CreateContext();
        var versions = new RowVersions(db);
        var seenByClient = (await versions.GetAsync<Customer>(customer.Id))!.Value;
        await RenameAsync(customer.Id, "Meyer");

        versions.Expect<Customer>(customer.Id, seenByClient);
        var loaded = await db.Customers.SingleAsync(c => c.Id == customer.Id);
        loaded.Rename("Anna", "Müller");

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ((IUnitOfWork)db).SaveChangesAsync());
        await using var readDb = postgres.CreateContext();
        Assert.Equal("Meyer", (await readDb.Customers.SingleAsync(c => c.Id == customer.Id)).LastName);
    }

    [Fact]
    public async Task The_current_version_as_expectation_commits_normally()
    {
        var customer = await SeedCustomerAsync();
        await using var db = postgres.CreateContext();
        var versions = new RowVersions(db);

        versions.Expect<Customer>(customer.Id, (await versions.GetAsync<Customer>(customer.Id))!.Value);
        var loaded = await db.Customers.SingleAsync(c => c.Id == customer.Id);
        loaded.Rename("Anna", "Müller");
        await ((IUnitOfWork)db).SaveChangesAsync();

        await using var readDb = postgres.CreateContext();
        Assert.Equal("Müller", (await readDb.Customers.SingleAsync(c => c.Id == customer.Id)).LastName);
    }

    private async Task RenameAsync(Guid customerId, string lastName)
    {
        await using var db = postgres.CreateContext();
        var customer = await db.Customers.SingleAsync(c => c.Id == customerId);
        customer.Rename("Anna", lastName);
        await db.SaveChangesAsync();
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
