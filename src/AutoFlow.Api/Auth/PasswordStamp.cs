using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AutoFlow.Domain.Users;

namespace AutoFlow.Api.Auth;

/// <summary>
/// Ties an access token to the password it was signed in with. The token
/// carries a keyed digest of the account's password hash; a new password
/// means a new hash (with a new salt), so every token issued before the
/// change stops matching and is refused. The digest is keyed with the
/// signing key, so it tells the token's holder nothing about the hash.
/// See ADR 0025.
/// </summary>
internal static class PasswordStamp
{
    private const int StampBytes = 16;

    public static string For(User user, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(options);

        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SigningKey), Encoding.UTF8.GetBytes(user.PasswordHash));
        return Base64Url.EncodeToString(digest.AsSpan(0, StampBytes));
    }

    /// <summary>Whether <paramref name="stamp"/> from a token belongs to the account's current password.</summary>
    public static bool Matches(User user, AuthOptions options, string? stamp) =>
        stamp is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(For(user, options)), Encoding.UTF8.GetBytes(stamp));
}
