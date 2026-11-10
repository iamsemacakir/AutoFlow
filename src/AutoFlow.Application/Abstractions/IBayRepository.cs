using AutoFlow.Domain.Bays;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Persistence contract for the workshop's bays. Bays are never deleted, so
/// there is no <c>Remove</c>. Entities returned by <see cref="GetByIdAsync"/>
/// are tracked: changes made to them are stored on the next
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IBayRepository
{
    Task<Bay?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// All bays, in and out of service, ordered by name. A workshop has a
    /// handful of bays, so the list is not paged. The returned entities are
    /// read-only snapshots and are not tracked.
    /// </summary>
    Task<IReadOnlyList<Bay>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>True if a bay already has exactly this (trimmed) name.</summary>
    Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default);

    void Add(Bay bay);
}
