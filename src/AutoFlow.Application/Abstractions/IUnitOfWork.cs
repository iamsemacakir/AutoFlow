namespace AutoFlow.Application.Abstractions;

/// <summary>
/// Commits all changes made through the repositories in one transaction.
/// Use cases call it exactly once, after every business rule has passed.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
