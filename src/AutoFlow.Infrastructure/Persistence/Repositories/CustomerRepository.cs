using System.Globalization;
using System.Text;
using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class CustomerRepository(AutoFlowDbContext db) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Customers.AnyAsync(c => c.Id == id, cancellationToken);

    public async Task<PagedList<Customer>> SearchAsync(
        IReadOnlyCollection<string> terms,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var matching = Matching(terms).AsNoTracking();

        var totalCount = await matching.CountAsync(cancellationToken);
        var items = await matching
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ThenBy(c => c.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<Customer>(items, page, pageSize, totalCount);
    }

    public void Add(Customer customer) => db.Customers.Add(customer);

    public void Remove(Customer customer) => db.Customers.Remove(customer);

    /// <summary>
    /// The search filter is written in SQL because the e-mail column is a
    /// value-converted <see cref="EmailAddress"/>, which LINQ cannot pattern-match.
    /// The terms are passed as parameters, never concatenated into the SQL.
    /// EF Core composes ordering, paging and the count on top of it, and the
    /// projection includes <c>xmin</c>, which <c>*</c> leaves out.
    /// </summary>
    private IQueryable<Customer> Matching(IReadOnlyCollection<string> terms)
    {
        if (terms.Count == 0)
        {
            return db.Customers;
        }

        var sql = new StringBuilder("SELECT c.*, c.xmin FROM customers AS c WHERE TRUE");
        var parameters = new object[terms.Count];
        var index = 0;
        foreach (var term in terms)
        {
            // Explicitly named, so they cannot clash with the parameters EF Core adds for paging.
            var name = string.Create(CultureInfo.InvariantCulture, $"search_term_{index}");
            parameters[index] = new NpgsqlParameter(name, "%" + EscapeLikePattern(term) + "%");
            sql.Append(
                CultureInfo.InvariantCulture,
                $" AND (c.first_name ILIKE @{name} OR c.last_name ILIKE @{name} OR c.email ILIKE @{name} OR c.phone_number ILIKE @{name})");
            index++;
        }

        return db.Customers.FromSqlRaw(sql.ToString(), parameters);
    }

    /// <summary>Makes <c>%</c>, <c>_</c> and <c>\</c> literal; backslash is PostgreSQL's default LIKE escape.</summary>
    private static string EscapeLikePattern(string term) =>
        term
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
