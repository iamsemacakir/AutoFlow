using AutoFlow.Application.Abstractions;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Tests;

[Collection(PostgresDefinition.Name)]
public class BayPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Added_bay_round_trips_in_service()
    {
        var bay = TestData.Bay();

        await using (var db = postgres.CreateContext())
        {
            new BayRepository(db).Add(bay);
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var loaded = await new BayRepository(readDb).GetByIdAsync(bay.Id);

        Assert.NotNull(loaded);
        Assert.Equal(bay.Name, loaded.Name);
        Assert.True(loaded.IsInService);
        Assert.Equal(TestData.Now, loaded.CreatedAt);
    }

    [Fact]
    public async Task Rename_and_service_state_are_saved_on_the_tracked_bay()
    {
        var bay = await SeedBayAsync();
        var newName = TestData.UniqueBayName();

        await using (var db = postgres.CreateContext())
        {
            var loaded = await new BayRepository(db).GetByIdAsync(bay.Id);
            loaded!.Rename(newName);
            loaded.TakeOutOfService();
            await db.SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await readDb.Bays.SingleAsync(b => b.Id == bay.Id);

        Assert.Equal(newName, reloaded.Name);
        Assert.False(reloaded.IsInService);
    }

    [Fact]
    public async Task ListAsync_returns_bays_in_and_out_of_service_by_name_without_tracking()
    {
        var prefix = Guid.NewGuid().ToString("N");
        var second = TestData.Bay($"{prefix} B");
        var first = TestData.Bay($"{prefix} A");
        second.TakeOutOfService();
        await using (var seedDb = postgres.CreateContext())
        {
            seedDb.Bays.AddRange(second, first);
            await seedDb.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        var bays = await new BayRepository(db).ListAsync();

        Assert.Collection(
            bays.Where(b => b.Name.StartsWith(prefix, StringComparison.Ordinal)),
            bay => Assert.Equal(first.Id, bay.Id),
            bay => Assert.False(bay.IsInService));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ExistsWithNameAsync_matches_the_trimmed_name_exactly()
    {
        var bay = await SeedBayAsync();

        await using var db = postgres.CreateContext();
        var repository = new BayRepository(db);

        Assert.True(await repository.ExistsWithNameAsync(bay.Name));
        Assert.True(await repository.ExistsWithNameAsync($"  {bay.Name} "));
        Assert.False(await repository.ExistsWithNameAsync(TestData.UniqueBayName()));
    }

    [Fact]
    public async Task Two_bays_with_the_same_name_violate_the_unique_index()
    {
        var bay = await SeedBayAsync();

        await using var db = postgres.CreateContext();
        db.Bays.Add(TestData.Bay(bay.Name));

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => ((IUnitOfWork)db).SaveChangesAsync());
        Assert.Equal("ux_bays_name", exception.ConstraintName);
    }

    private async Task<Domain.Bays.Bay> SeedBayAsync()
    {
        var bay = TestData.Bay();
        await using var db = postgres.CreateContext();
        db.Bays.Add(bay);
        await db.SaveChangesAsync();
        return bay;
    }
}
