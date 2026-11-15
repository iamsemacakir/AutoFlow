using AutoFlow.Domain.Common;

namespace AutoFlow.Domain.Users;

/// <summary>
/// The rules a new password must meet. Length is what makes a password
/// strong, so that is all that is required (in line with NIST SP 800-63B):
/// no composition rules, no expiry.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <exception cref="DomainException">The password is too short or too long.</exception>
    public static void Check(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinLength)
        {
            throw new DomainException($"A password needs at least {MinLength} characters.");
        }

        if (password.Length > MaxLength)
        {
            throw new DomainException($"A password can have at most {MaxLength} characters.");
        }
    }
}
