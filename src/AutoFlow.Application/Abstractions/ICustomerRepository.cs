using AutoFlow.Domain.Customers;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Persistence contract for customers. Entities returned by
/// <see cref="GetByIdAsync"/> are tracked: changes made to them are stored on
/// the next <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Customer customer);
}
