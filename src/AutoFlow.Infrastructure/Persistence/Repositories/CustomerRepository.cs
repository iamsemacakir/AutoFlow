using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class CustomerRepository(AutoFlowDbContext db) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Customers.AnyAsync(c => c.Id == id, cancellationToken);

    public void Add(Customer customer) => db.Customers.Add(customer);
}
