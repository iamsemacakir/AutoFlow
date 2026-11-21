using System.Globalization;
using System.Security.Claims;
using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Users;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AutoFlow.Api.Auth;

/// <summary>
/// Issues HMAC-SHA256 signed JWTs naming the user, their e-mail address,
/// display name and role. The role in the token is a hint for clients; the
/// API reads the current role from the database on every request. The
/// token also carries the <see cref="PasswordStamp"/>, so changing the
/// password ends every session signed in with the old one.
/// </summary>
internal sealed class JwtAccessTokenIssuer(AuthOptions options, TimeProvider timeProvider) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials = new(options.CreateSigningKey(), SecurityAlgorithms.HmacSha256);

    public AccessToken Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = timeProvider.GetUtcNow();
        var expiresAt = now + options.TokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(AuthClaims.Subject, user.Id.ToString()),
                new Claim(AuthClaims.Email, user.Email.Value),
                new Claim(AuthClaims.Name, user.DisplayName),
                new Claim(AuthClaims.Role, user.Role.ToString()),
                new Claim(AuthClaims.PasswordStamp, PasswordStamp.For(user, options)),
                new Claim(AuthClaims.TokenId, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)),
            ]),
        });

        return new AccessToken(token, expiresAt);
    }
}
