namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Commits all changes made through the repositories in one transaction.
/// Use cases call it exactly once, after every business rule has passed.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Commits the pending changes atomically.</summary>
    /// <exception cref="ConcurrencyConflictException">
    /// A loaded aggregate was changed by another request in the meantime.
    /// </exception>
    /// <exception cref="UniqueConstraintViolationException">
    /// A unique value was taken by another request in the meantime.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
