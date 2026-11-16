using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Common;
using AutoFlow.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence;

/// <summary>
/// Reads an aggregate's <c>xmin</c> (the <c>Version</c> shadow property) and
/// hands expected versions to <see cref="AutoFlowDbContext"/>, which applies
/// them to the concurrency token before it commits.
/// </summary>
internal sealed class RowVersions(AutoFlowDbContext db) : IRowVersions
{
    public Task<uint?> GetAsync<TAggregate>(Guid id, CancellationToken cancellationToken = default)
        where TAggregate : Entity =>
        db.Set<TAggregate>()
            .AsNoTracking()
            .Where(aggregate => aggregate.Id == id)
            .Select(aggregate => (uint?)EF.Property<uint>(aggregate, RowVersionExtensions.PropertyName))
            .SingleOrDefaultAsync(cancellationToken);

    public void Expect<TAggregate>(Guid id, uint version)
        where TAggregate : Entity =>
        db.ExpectVersion(typeof(TAggregate), id, version);
}
