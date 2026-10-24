using AutoFlow.Domain.Customers;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Tests;

/// <summary>
/// The database is shared by all tests, so every test searches for a token
/// only its own customers carry.
/// </summary>
[Collection(PostgresDefinition.Name)]
public class CustomerSearchTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Each_term_matches_any_field_case_insensitively()
    {
        var token = UniqueToken();
        var byFirstName = Customer(token + "Anna", "Schmidt", "anna@example.com", null);
        var byEmail = Customer("Jonas", "Becker", $"{token}@example.com", null);
        var byPhone = Customer("Mia", "Wagner", "mia@example.com", "+49 89 " + Digits(token));
        await SeedAsync(byFirstName, byEmail, byPhone);

        Assert.Equal(new[] { byFirstName.Id }, await SearchIdsAsync(token.ToUpperInvariant() + "ANNA"));
        Assert.Equal(new[] { byEmail.Id }, await SearchIdsAsync(token.ToUpperInvariant() + "@EXAMPLE"));
        Assert.Equal(new[] { byPhone.Id }, await SearchIdsAsync(Digits(token)));
    }

    [Fact]
    public async Task All_terms_must_match()
    {
        var token = UniqueToken();
        var annaSchmidt = Customer("Anna", token + "Schmidt", "a@example.com", null);
        await SeedAsync(annaSchmidt, Customer("Jonas", token + "Schmidt", "j@example.com", null));

        Assert.Equal(new[] { annaSchmidt.Id }, await SearchIdsAsync(token, "anna"));
    }

    [Fact]
    public async Task Wildcards_in_terms_are_matched_literally()
    {
        var token = UniqueToken();
        var literal = Customer("Anna", token + "_1%", "a@example.com", null);
        var wildcardMatch = Customer("Anna", token + "x1y", "b@example.com", null);
        var backslash = Customer("Anna", token + @"\z", "c@example.com", null);
        await SeedAsync(literal, wildcardMatch, backslash);

        Assert.Equal(new[] { literal.Id }, await SearchIdsAsync(token + "_1%"));
        Assert.Equal(new[] { backslash.Id }, await SearchIdsAsync(token + @"\z"));
    }

    [Fact]
    public async Task Results_are_ordered_by_name_and_paged_with_a_total_count()
    {
        var token = UniqueToken();
        var wagner = Customer("Mia", token + "Wagner", "m@example.com", null);
        var beckerJonas = Customer("Jonas", token + "Becker", "j@example.com", null);
        var beckerAnna = Customer("Anna", token + "Becker", "a@example.com", null);
        await SeedAsync(wagner, beckerJonas, beckerAnna);

        await using var db = postgres.CreateContext();
        var repository = new CustomerRepository(db);
        var first = await repository.SearchAsync([token], 1, 2);
        var second = await repository.SearchAsync([token], 2, 2);

        Assert.Equal(new[] { beckerAnna.Id, beckerJonas.Id }, first.Items.Select(c => c.Id));
        Assert.Equal(new[] { wagner.Id }, second.Items.Select(c => c.Id));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
    }

    [Fact]
    public async Task Search_results_are_not_tracked()
    {
        var token = UniqueToken();
        await SeedAsync(Customer("Anna", token, "a@example.com", null));

        await using var db = postgres.CreateContext();
        var page = await new CustomerRepository(db).SearchAsync([token], 1, 10);

        Assert.Single(page.Items);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Search_without_terms_counts_every_customer()
    {
        await SeedAsync(Customer("Anna", UniqueToken(), "a@example.com", null));

        await using var db = postgres.CreateContext();
        var page = await new CustomerRepository(db).SearchAsync([], 1, 5);

        Assert.Equal(await db.Customers.CountAsync(), page.TotalCount);
        Assert.InRange(page.Items.Count, 1, 5);
    }

    /// <summary>Letters and digits, so the token is neither a LIKE wildcard nor split into terms.</summary>
    private static string UniqueToken() => "t" + Guid.NewGuid().ToString("N")[..12];

    private static string Digits(string token) =>
        string.Concat(token.Select(c => (char)('0' + (c % 10))));

    private static Customer Customer(string firstName, string lastName, string email, string? phone) =>
        Domain.Customers.Customer.Create(firstName, lastName, email, phone, TestData.Now);

    private async Task SeedAsync(params Customer[] customers)
    {
        await using var db = postgres.CreateContext();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
    }

    private async Task<Guid[]> SearchIdsAsync(params string[] terms)
    {
        await using var db = postgres.CreateContext();
        var page = await new CustomerRepository(db).SearchAsync(terms, 1, 50);
        return [.. page.Items.Select(c => c.Id)];
    }
}
