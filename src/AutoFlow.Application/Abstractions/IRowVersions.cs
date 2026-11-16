using AutoFlow.Domain.Common;

namespace AutoFlow.Application.Abstractions;

/// <summary>
/// The stored version of an aggregate, for HTTP conditional requests: the API
/// sends it as an <c>ETag</c> and checks a client's <c>If-Match</c> against it
/// before a change (ADR 0024). A version changes with every committed change
/// of the aggregate's root row; it means nothing beyond equality.
/// </summary>
public interface IRowVersions
{
    /// <summary>The current version, or <see langword="null"/> when no such aggregate exists.</summary>
    Task<uint?> GetAsync<TAggregate>(Guid id, CancellationToken cancellationToken = default)
        where TAggregate : Entity;

    /// <summary>
    /// Requires the next commit to find the aggregate still at
    /// <paramref name="version"/>; if another request changed it in between,
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> throws
    /// <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    void Expect<TAggregate>(Guid id, uint version)
        where TAggregate : Entity;
}
