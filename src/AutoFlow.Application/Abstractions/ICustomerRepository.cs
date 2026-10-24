using AutoFlow.Application.Common;
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

    /// <summary>
    /// Returns one page of customers ordered by last name, first name and id.
    /// A customer matches when every term occurs, case-insensitively, in its
    /// first name, last name, e-mail address or phone number (terms are
    /// literal text, not patterns). No terms match every customer. The
    /// returned entities are read-only snapshots and are not tracked.
    /// </summary>
    Task<PagedList<Customer>> SearchAsync(
        IReadOnlyCollection<string> terms,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    void Add(Customer customer);

    /// <summary>Deletes a tracked customer on the next <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Remove(Customer customer);
}
