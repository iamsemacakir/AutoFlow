using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Bays;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Persistence.Repositories;

internal sealed class BayRepository(AutoFlowDbContext db) : IBayRepository
{
    public Task<Bay?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Bays.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Bay>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Bays
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        return db.Bays.AnyAsync(b => b.Name == trimmed, cancellationToken);
    }

    public void Add(Bay bay) => db.Bays.Add(bay);
}
