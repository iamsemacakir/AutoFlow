using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Persistence contract for staff accounts. Users are deactivated, never
/// deleted. Entities returned by <see cref="GetByIdAsync"/> and
/// <see cref="GetByEmailAsync"/> are tracked.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    Task<bool> ExistsWithEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>True once any account exists, active or not.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>Active owners: the workshop must always keep at least one.</summary>
    Task<int> CountActiveOwnersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// All accounts ordered by display name. A workshop has a handful of
    /// staff, so the list is not paged. The returned entities are not tracked.
    /// </summary>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    void Add(User user);
}
