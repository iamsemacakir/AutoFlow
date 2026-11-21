using System.Security.Claims;

namespace AutoFlow.Api.Auth;

/// <summary>
/// The claims an access token carries, under their short JWT names (the
/// bearer handler keeps them as they are instead of mapping them to long
/// XML schema URIs).
/// </summary>
public static class AuthClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string TokenId = "jti";

    /// <summary>Which password the token was signed in with (<see cref="PasswordStamp"/>).</summary>
    public const string PasswordStamp = "pwd";

    /// <summary>The signed-in user's id, from the token's subject.</summary>
    /// <exception cref="InvalidOperationException">The principal is not an authenticated AutoFlow user.</exception>
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Guid.TryParse(principal.FindFirstValue(Subject), out var id)
            ? id
            : throw new InvalidOperationException("The request is not from a signed-in user.");
    }
}
