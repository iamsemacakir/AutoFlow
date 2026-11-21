using System.Security.Claims;
using AutoFlow.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AutoFlow.Api.Auth;

internal static class AuthenticationSetup
{
    /// <summary>
    /// Bearer-token authentication with tokens from <see cref="JwtAccessTokenIssuer"/>.
    /// Every request re-reads the account: a deactivated user's tokens stop
    /// working at once, a role change takes effect on the next request, and
    /// a changed password ends every session signed in with the old one.
    /// </summary>
    public static AuthOptions AddAutoFlowAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = (configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions()).Validated();

        services.AddSingleton(options);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = options.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthClaims.Name,
                    RoleClaimType = AuthClaims.Role,
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = CheckAccountAsync };
            });

        return options;
    }

    private static async Task CheckAccountAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity
            || !Guid.TryParse(identity.FindFirst(AuthClaims.Subject)?.Value, out var userId))
        {
            context.Fail("The token does not name a user.");
            return;
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
        if (user is not { IsActive: true })
        {
            context.Fail("The account no longer exists or has been deactivated.");
            return;
        }

        var options = context.HttpContext.RequestServices.GetRequiredService<AuthOptions>();
        if (!PasswordStamp.Matches(user, options, identity.FindFirst(AuthClaims.PasswordStamp)?.Value))
        {
            context.Fail("The password has been changed since the token was issued.");
            return;
        }

        foreach (var stale in identity.FindAll(AuthClaims.Role).ToList())
        {
            identity.RemoveClaim(stale);
        }

        identity.AddClaim(new Claim(AuthClaims.Role, user.Role.ToString()));
    }
}
