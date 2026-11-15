using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Abstractions;

/// <summary>Issues the bearer token a signed-in user sends with every request.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(User user);
}

/// <param name="Value">The token to send as <c>Authorization: Bearer …</c>.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
