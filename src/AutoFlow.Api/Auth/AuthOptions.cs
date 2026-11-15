using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AutoFlow.Api.Auth;

/// <summary>
/// How access tokens are issued and checked, bound from the <c>Auth</c>
/// configuration section. The signing key is a secret: it comes from the
/// environment (<c>Auth__SigningKey</c>) outside development, and the API
/// refuses to start without one. See ADR 0021.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The longest token lifetime the API accepts in configuration.</summary>
    public static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromDays(31);

    private const int MinSigningKeyBytes = 32;

    public string Issuer { get; init; } = "autoflow";

    public string Audience { get; init; } = "autoflow";

    /// <summary>HMAC-SHA256 key, at least 32 bytes of UTF-8.</summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>How long a token is accepted; a working day by default.</summary>
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(8);

    /// <summary>The first owner of a new installation; ignored once any account exists.</summary>
    public InitialOwnerOptions? InitialOwner { get; init; }

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <exception cref="InvalidOperationException">The key is missing or too short, or the lifetime is out of range.</exception>
    public AuthOptions Validated()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey ?? string.Empty) < MinSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SigningKey)} must be at least {MinSigningKeyBytes} bytes. Set Auth__SigningKey " +
                "to a long random value (appsettings.Development.json has one for local development only).");
        }

        if (TokenLifetime <= TimeSpan.Zero || TokenLifetime > MaxTokenLifetime)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(TokenLifetime)} must be positive and at most {MaxTokenLifetime.TotalDays} days.");
        }

        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(Issuer)} and {SectionName}:{nameof(Audience)} must not be blank.");
        }

        return this;
    }
}

/// <summary>The account created on first start when the database has none.</summary>
public sealed class InitialOwnerOptions
{
    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = "Owner";

    public string Password { get; init; } = string.Empty;
}
