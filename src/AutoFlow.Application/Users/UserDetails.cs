using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Users;

/// <summary>
/// Read model of a staff account. The role is its name (<c>"ServiceAdvisor"</c>);
/// the password hash is never part of it.
/// </summary>
public sealed record UserDetails(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt)
{
    public static UserDetails From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new(
            user.Id,
            user.Email.Value,
            user.DisplayName,
            user.Role.ToString(),
            user.IsActive,
            user.CreatedAt,
            user.LastSignInAt);
    }
}
