using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Common;
using AutoFlow.Domain.Customers;

namespace AutoFlow.Application.Users;

public sealed record SignInCommand(string Email, string Password);

/// <summary>A successful sign-in: the bearer token and who it belongs to.</summary>
public sealed record SignedIn(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserDetails User);

/// <summary>
/// Checks an e-mail address and password and issues an access token. Every
/// failure — unknown address, wrong password, deactivated account — is the
/// same <c>auth.invalid_credentials</c> (401), and an unknown address still
/// costs one hash verification, so neither the answer nor its timing tells
/// whether an account exists.
/// </summary>
public sealed class SignInHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer tokenIssuer,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    // Verified against when the address is unknown, to spend the same time as a real check.
    private static string? _decoyHash;

    public async Task<Result<SignedIn>> HandleAsync(SignInCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = TryParseEmail(command.Email) is { } email
            ? await users.GetByEmailAsync(email, cancellationToken)
            : null;

        if (user is null)
        {
            _decoyHash ??= passwordHasher.Hash("decoy password for unknown accounts");
            passwordHasher.Verify(_decoyHash, command.Password ?? string.Empty);
            return Result.Failure<SignedIn>(UserErrors.InvalidCredentials());
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.Password ?? string.Empty) || !user.IsActive)
        {
            return Result.Failure<SignedIn>(UserErrors.InvalidCredentials());
        }

        user.RecordSignIn(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokenIssuer.Issue(user);
        return Result.Success(new SignedIn(token.Value, "Bearer", token.ExpiresAt, UserDetails.From(user)));
    }

    private static EmailAddress? TryParseEmail(string? email)
    {
        try
        {
            return EmailAddress.Create(email);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
