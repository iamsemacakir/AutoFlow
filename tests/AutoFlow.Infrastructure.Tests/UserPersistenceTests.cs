using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Users;
using AutoFlow.Infrastructure.Persistence.Configurations;
using AutoFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AutoFlow.Infrastructure.Tests;

[Collection(PostgresDefinition.Name)]
public class UserPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task User_round_trips_and_is_found_by_its_normalized_email()
    {
        var email = UniqueEmail();
        var user = User.Create(email.ToUpperInvariant(), "Lena Weber", UserRole.ServiceAdvisor, "pbkdf2-sha512$1$c2FsdA==$aGFzaA==", TestData.Now);

        await using (var db = postgres.CreateContext())
        {
            new UserRepository(db).Add(user);
            await ((IUnitOfWork)db).SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var repository = new UserRepository(readDb);
        var loaded = await repository.GetByEmailAsync(EmailAddress.Create(email));
        var role = await readDb.Database
            .SqlQuery<string>($"SELECT role AS \"Value\" FROM users WHERE id = {user.Id}")
            .SingleAsync();

        Assert.NotNull(loaded);
        Assert.Equal((user.Id, email, "Lena Weber"), (loaded.Id, loaded.Email.Value, loaded.DisplayName));
        Assert.Equal((UserRole.ServiceAdvisor, true), (loaded.Role, loaded.IsActive));
        Assert.Equal(user.PasswordHash, loaded.PasswordHash);
        Assert.Equal("ServiceAdvisor", role);
        Assert.True(await repository.ExistsWithEmailAsync(EmailAddress.Create(email)));
        Assert.False(await repository.ExistsWithEmailAsync(EmailAddress.Create(UniqueEmail())));
    }

    [Fact]
    public async Task Second_account_with_the_same_email_violates_the_unique_index()
    {
        var email = UniqueEmail();
        await SeedAsync(User.Create(email, "Lena Weber", UserRole.Mechanic, "hash", TestData.Now));

        await using var db = postgres.CreateContext();
        db.Users.Add(User.Create(email, "Someone Else", UserRole.Owner, "hash", TestData.Now));

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => ((IUnitOfWork)db).SaveChangesAsync());
        Assert.Equal(UserConfiguration.EmailIndex, exception.ConstraintName);
    }

    [Fact]
    public async Task Changes_to_a_tracked_user_are_saved_and_active_owners_are_counted()
    {
        await using var countDb = postgres.CreateContext();
        var before = await new UserRepository(countDb).CountActiveOwnersAsync();
        var owner = await SeedAsync(User.Create(UniqueEmail(), "Owner", UserRole.Owner, "hash", TestData.Now));
        var mechanic = await SeedAsync(User.Create(UniqueEmail(), "Mechanic", UserRole.Mechanic, "hash", TestData.Now));

        await using (var db = postgres.CreateContext())
        {
            var repository = new UserRepository(db);
            Assert.Equal(before + 1, await repository.CountActiveOwnersAsync());

            var tracked = await repository.GetByIdAsync(owner.Id);
            tracked!.Deactivate();
            var promoted = await repository.GetByIdAsync(mechanic.Id);
            promoted!.ChangeRole(UserRole.Owner);
            promoted.RecordSignIn(TestData.Now.AddHours(1));
            await ((IUnitOfWork)db).SaveChangesAsync();
        }

        await using var readDb = postgres.CreateContext();
        var reloaded = await new UserRepository(readDb).GetByIdAsync(mechanic.Id);
        Assert.Equal(before + 1, await new UserRepository(readDb).CountActiveOwnersAsync());
        Assert.Equal(UserRole.Owner, reloaded!.Role);
        Assert.Equal<DateTimeOffset?>(TestData.Now.AddHours(1), reloaded.LastSignInAt);
        Assert.False((await new UserRepository(readDb).GetByIdAsync(owner.Id))!.IsActive);
    }

    [Fact]
    public async Task ListAsync_returns_all_users_by_display_name_without_tracking()
    {
        var prefix = Guid.NewGuid().ToString("N");
        await SeedAsync(User.Create(UniqueEmail(), $"{prefix} B", UserRole.Mechanic, "hash", TestData.Now));
        var inactive = User.Create(UniqueEmail(), $"{prefix} A", UserRole.Mechanic, "hash", TestData.Now);
        inactive.Deactivate();
        await SeedAsync(inactive);

        await using var db = postgres.CreateContext();
        var users = await new UserRepository(db).ListAsync();

        var ours = users.Where(u => u.DisplayName.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        Assert.Equal([$"{prefix} A", $"{prefix} B"], ours.Select(u => u.DisplayName).ToArray());
        Assert.True(await new UserRepository(db).AnyAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<User> SeedAsync(User user)
    {
        await using var db = postgres.CreateContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
