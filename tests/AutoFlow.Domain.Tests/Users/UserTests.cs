using AutoFlow.Domain.Common;
using AutoFlow.Domain.Users;

namespace AutoFlow.Domain.Tests.Users;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser(UserRole role = UserRole.ServiceAdvisor) =>
        User.Create("  Lena.Weber@Example.com ", "  Lena Weber ", role, "hash", Now);

    [Fact]
    public void Create_normalizes_the_login_and_starts_active()
    {
        var user = NewUser();

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("lena.weber@example.com", user.Email.Value);
        Assert.Equal("Lena Weber", user.DisplayName);
        Assert.Equal(UserRole.ServiceAdvisor, user.Role);
        Assert.Equal("hash", user.PasswordHash);
        Assert.True(user.IsActive);
        Assert.Equal(Now, user.CreatedAt);
        Assert.Null(user.LastSignInAt);
    }

    [Theory]
    [InlineData("not-an-email", "Lena", "hash")]
    [InlineData("lena@example.com", " ", "hash")]
    [InlineData("lena@example.com", "Lena", "")]
    public void Create_rejects_invalid_input(string email, string displayName, string hash)
    {
        Assert.Throws<DomainException>(() => User.Create(email, displayName, UserRole.Mechanic, hash, Now));
    }

    [Fact]
    public void Create_rejects_an_unknown_role()
    {
        Assert.Throws<DomainException>(() => User.Create("lena@example.com", "Lena", (UserRole)42, "hash", Now));
    }

    [Fact]
    public void Role_name_and_password_hash_can_change()
    {
        var user = NewUser();

        user.ChangeRole(UserRole.Owner);
        user.Rename("Lena W.");
        user.ChangePasswordHash("new-hash");

        Assert.Equal((UserRole.Owner, "Lena W.", "new-hash"), (user.Role, user.DisplayName, user.PasswordHash));
    }

    [Fact]
    public void Deactivated_user_cannot_sign_in_until_reactivated()
    {
        var user = NewUser();

        user.Deactivate();

        Assert.False(user.IsActive);
        Assert.Throws<DomainException>(() => user.Deactivate());
        Assert.Throws<DomainException>(() => user.RecordSignIn(Now));

        user.Reactivate();
        user.RecordSignIn(Now.AddHours(1));

        Assert.True(user.IsActive);
        Assert.Equal<DateTimeOffset?>(Now.AddHours(1), user.LastSignInAt);
        Assert.Throws<DomainException>(() => user.Reactivate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    [InlineData("too-short")]
    public void Password_policy_requires_ten_characters(string? password)
    {
        Assert.Throws<DomainException>(() => PasswordPolicy.Check(password));
    }

    [Fact]
    public void Password_policy_accepts_long_passphrases_up_to_the_limit()
    {
        PasswordPolicy.Check("correct horse battery staple");
        PasswordPolicy.Check(new string('x', PasswordPolicy.MaxLength));

        Assert.Throws<DomainException>(() => PasswordPolicy.Check(new string('x', PasswordPolicy.MaxLength + 1)));
    }
}
