using AutoFlow.Domain.Common;
using AutoFlow.Domain.Customers;

namespace AutoFlow.Domain.Users;

/// <summary>
/// A member of staff who signs in to AutoFlow. The e-mail address is the
/// login and never changes; the role decides what the user may do. Users
/// are deactivated rather than deleted, so the history of who did what
/// stays readable. The password itself never reaches the domain: the user
/// holds a hash produced by the application's password hasher.
/// </summary>
public sealed class User : Entity
{
    public const int DisplayNameMaxLength = 100;
    public const int PasswordHashMaxLength = 200;

    private User(
        Guid id,
        EmailAddress email,
        string displayName,
        UserRole role,
        string passwordHash,
        DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
        Role = role;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
        IsActive = true;
    }

    /// <summary>The login, normalized to lower case.</summary>
    public EmailAddress Email { get; }

    /// <summary>The name shown in the application, e.g. on a work order.</summary>
    public string DisplayName { get; private set; }

    public UserRole Role { get; private set; }

    public string PasswordHash { get; private set; }

    /// <summary>False once deactivated: the user can no longer sign in.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? LastSignInAt { get; private set; }

    public static User Create(
        string email,
        string displayName,
        UserRole role,
        string passwordHash,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            EmailAddress.Create(email),
            Guard.RequiredText(displayName, "Display name", DisplayNameMaxLength),
            EnsureDefined(role),
            EnsureHash(passwordHash),
            createdAt);

    public void Rename(string displayName) =>
        DisplayName = Guard.RequiredText(displayName, "Display name", DisplayNameMaxLength);

    public void ChangeRole(UserRole role) => Role = EnsureDefined(role);

    public void ChangePasswordHash(string passwordHash) => PasswordHash = EnsureHash(passwordHash);

    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new DomainException("The user is already deactivated.");
        }

        IsActive = false;
    }

    public void Reactivate()
    {
        if (IsActive)
        {
            throw new DomainException("The user is already active.");
        }

        IsActive = true;
    }

    /// <summary>Notes a successful sign-in. A deactivated user cannot sign in.</summary>
    public void RecordSignIn(DateTimeOffset signedInAt)
    {
        if (!IsActive)
        {
            throw new DomainException("A deactivated user cannot sign in.");
        }

        LastSignInAt = signedInAt;
    }

    private static UserRole EnsureDefined(UserRole role) =>
        Enum.IsDefined(role) ? role : throw new DomainException("Unknown role.");

    private static string EnsureHash(string passwordHash) =>
        string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > PasswordHashMaxLength
            ? throw new DomainException("A user needs a valid password hash.")
            : passwordHash;
}
