using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Application.Users;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Tests.Users;

public class UserHandlerTests
{
    private const string Password = "correct horse battery";

    private readonly InMemoryStore _store = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FixedTimeProvider _time = new();

    private SignInHandler SignIn() => new(_store, _hasher, new FakeAccessTokenIssuer(), _store, _time);

    private User SeedUser(UserRole role = UserRole.ServiceAdvisor, string email = "lena@example.com", bool active = true)
    {
        var user = User.Create(email, "Lena Weber", role, _hasher.Hash(Password), FixedTimeProvider.Default);
        if (!active)
        {
            user.Deactivate();
        }

        return _store.Seed(user);
    }

    [Fact]
    public async Task Sign_in_with_the_right_password_issues_a_token_and_records_the_time()
    {
        var user = SeedUser();

        var result = await SignIn().HandleAsync(new SignInCommand(" LENA@example.com ", Password));

        Assert.True(result.IsSuccess);
        Assert.Equal($"token-for:{user.Id}:ServiceAdvisor", result.Value.AccessToken);
        Assert.Equal("Bearer", result.Value.TokenType);
        Assert.Equal(FixedTimeProvider.Default.AddHours(8), result.Value.ExpiresAt);
        Assert.Equal(("lena@example.com", "ServiceAdvisor"), (result.Value.User.Email, result.Value.User.Role));
        Assert.Equal<DateTimeOffset?>(FixedTimeProvider.Default, user.LastSignInAt);
        Assert.Equal(1, _store.SaveCount);
    }

    [Theory]
    [InlineData("lena@example.com", "wrong password")]
    [InlineData("nobody@example.com", Password)]
    [InlineData("not an email", Password)]
    public async Task Sign_in_failures_all_look_the_same(string email, string password)
    {
        SeedUser();

        var result = await SignIn().HandleAsync(new SignInCommand(email, password));

        Assert.Equal(("auth.invalid_credentials", ErrorKind.Unauthorized), (result.Error.Code, result.Error.Kind));
        Assert.Equal(1, _hasher.Verifications);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in_even_with_the_right_password()
    {
        SeedUser(active: false);

        var result = await SignIn().HandleAsync(new SignInCommand("lena@example.com", Password));

        Assert.Equal("auth.invalid_credentials", result.Error.Code);
    }

    [Fact]
    public async Task Create_opens_an_account_with_a_hashed_password()
    {
        var handler = new CreateUserHandler(_store, _hasher, _store, _time);

        var result = await handler.HandleAsync(new CreateUserCommand("Tom@Example.com", "Tom Fischer", "mechanic", "long enough password"));

        Assert.True(result.IsSuccess);
        Assert.Equal(("tom@example.com", "Tom Fischer", "Mechanic", true), (result.Value.Email, result.Value.DisplayName, result.Value.Role, result.Value.IsActive));
        Assert.Equal("hashed:long enough password", Assert.Single(_store.Users).PasswordHash);
    }

    [Fact]
    public async Task Create_with_an_email_in_use_or_an_unknown_role_fails()
    {
        SeedUser();
        var handler = new CreateUserHandler(_store, _hasher, _store, _time);

        var duplicate = await handler.HandleAsync(new CreateUserCommand("LENA@example.com", "Lena Two", "Mechanic", Password));
        var unknownRole = await handler.HandleAsync(new CreateUserCommand("new@example.com", "New", "Apprentice", Password));

        Assert.Equal(("user.duplicate_email", ErrorKind.Conflict), (duplicate.Error.Code, duplicate.Error.Kind));
        Assert.Equal(("user.unknown_role", ErrorKind.Validation), (unknownRole.Error.Code, unknownRole.Error.Kind));
        Assert.Single(_store.Users);
    }

    [Fact]
    public async Task Create_with_a_short_password_breaks_the_policy()
    {
        var handler = new CreateUserHandler(_store, _hasher, _store, _time);

        await Assert.ThrowsAsync<DomainException>(
            () => handler.HandleAsync(new CreateUserCommand("tom@example.com", "Tom", "Mechanic", "short")));
        Assert.Empty(_store.Users);
    }

    [Fact]
    public async Task Initial_owner_is_created_only_while_there_are_no_accounts()
    {
        var handler = new EnsureInitialOwnerHandler(_store, _hasher, _store, _time);

        var first = await handler.HandleAsync(new EnsureInitialOwnerCommand("owner@example.com", "Owner", Password));
        var second = await handler.HandleAsync(new EnsureInitialOwnerCommand("other@example.com", "Other", Password));

        Assert.True(first);
        Assert.False(second);
        var owner = Assert.Single(_store.Users);
        Assert.Equal((UserRole.Owner, "owner@example.com"), (owner.Role, owner.Email.Value));
    }

    [Fact]
    public async Task Last_active_owner_can_neither_be_demoted_nor_deactivated()
    {
        var owner = SeedUser(UserRole.Owner, "owner@example.com");
        var advisor = SeedUser(UserRole.ServiceAdvisor, "advisor@example.com");
        var changeRole = new ChangeUserRoleHandler(_store, _store);
        var deactivate = new DeactivateUserHandler(_store, _store);

        var demoted = await changeRole.HandleAsync(new ChangeUserRoleCommand(owner.Id, "Mechanic"));
        var deactivated = await deactivate.HandleAsync(new DeactivateUserCommand(owner.Id, advisor.Id));

        Assert.Equal(("user.last_owner", ErrorKind.Conflict), (demoted.Error.Code, demoted.Error.Kind));
        Assert.Equal("user.last_owner", deactivated.Error.Code);
        Assert.Equal((UserRole.Owner, true), (owner.Role, owner.IsActive));

        // With a second owner both are fine.
        var promoted = await changeRole.HandleAsync(new ChangeUserRoleCommand(advisor.Id, "Owner"));
        var nowDemoted = await changeRole.HandleAsync(new ChangeUserRoleCommand(owner.Id, "Mechanic"));

        Assert.Equal("Owner", promoted.Value.Role);
        Assert.Equal("Mechanic", nowDemoted.Value.Role);
    }

    [Fact]
    public async Task Nobody_can_deactivate_themselves()
    {
        var owner = SeedUser(UserRole.Owner, "owner@example.com");
        SeedUser(UserRole.Owner, "second@example.com");

        var result = await new DeactivateUserHandler(_store, _store).HandleAsync(new DeactivateUserCommand(owner.Id, owner.Id));

        Assert.Equal(("user.cannot_deactivate_self", ErrorKind.Conflict), (result.Error.Code, result.Error.Kind));
        Assert.True(owner.IsActive);
    }

    [Fact]
    public async Task Deactivated_user_can_be_reactivated()
    {
        var owner = SeedUser(UserRole.Owner, "owner@example.com");
        var mechanic = SeedUser(UserRole.Mechanic, "tom@example.com");

        var deactivated = await new DeactivateUserHandler(_store, _store).HandleAsync(new DeactivateUserCommand(mechanic.Id, owner.Id));
        var reactivated = await new ReactivateUserHandler(_store, _store).HandleAsync(new ReactivateUserCommand(mechanic.Id));

        Assert.False(deactivated.Value.IsActive);
        Assert.True(reactivated.Value.IsActive);
        await Assert.ThrowsAsync<DomainException>(
            () => new ReactivateUserHandler(_store, _store).HandleAsync(new ReactivateUserCommand(mechanic.Id)));
    }

    [Fact]
    public async Task Handlers_report_unknown_users()
    {
        var id = Guid.NewGuid();

        var get = await new GetUserHandler(_store).HandleAsync(new GetUserQuery(id));
        var role = await new ChangeUserRoleHandler(_store, _store).HandleAsync(new ChangeUserRoleCommand(id, "Owner"));
        var deactivate = await new DeactivateUserHandler(_store, _store).HandleAsync(new DeactivateUserCommand(id, Guid.NewGuid()));
        var reactivate = await new ReactivateUserHandler(_store, _store).HandleAsync(new ReactivateUserCommand(id));

        Assert.All(
            new[] { get.Error, role.Error, deactivate.Error, reactivate.Error },
            error => Assert.Equal("user.not_found", error.Code));
    }

    [Fact]
    public async Task Change_password_needs_the_current_one()
    {
        var user = SeedUser();
        var handler = new ChangePasswordHandler(_store, _hasher, _store);

        var wrong = await handler.HandleAsync(new ChangePasswordCommand(user.Id, "not my password", "brand new password"));
        var changed = await handler.HandleAsync(new ChangePasswordCommand(user.Id, Password, "brand new password"));

        Assert.Equal(("auth.wrong_current_password", ErrorKind.Validation), (wrong.Error.Code, wrong.Error.Kind));
        Assert.True(changed.IsSuccess);
        Assert.Equal("hashed:brand new password", user.PasswordHash);
        await Assert.ThrowsAsync<DomainException>(
            () => handler.HandleAsync(new ChangePasswordCommand(user.Id, "brand new password", "short")));
    }

    [Fact]
    public async Task List_returns_every_account_by_display_name()
    {
        _store.Seed(User.Create("b@example.com", "Bernd", UserRole.Mechanic, "hash", FixedTimeProvider.Default));
        _store.Seed(User.Create("a@example.com", "Anna", UserRole.Owner, "hash", FixedTimeProvider.Default));

        var users = await new ListUsersHandler(_store).HandleAsync(new ListUsersQuery());

        Assert.Equal(["Anna", "Bernd"], users.Select(u => u.DisplayName).ToArray());
    }
}
