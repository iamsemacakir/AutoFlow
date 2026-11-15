using AutoFlow.Application.Abstractions;
using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Users;

public sealed record EnsureInitialOwnerCommand(string Email, string DisplayName, string Password);

/// <summary>
/// Creates the first owner of a new installation from configuration, so the
/// first person can sign in. Does nothing once any account exists, so the
/// configured password cannot be used to take over a running system.
/// Returns whether an account was created.
/// </summary>
public sealed class EnsureInitialOwnerHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<bool> HandleAsync(EnsureInitialOwnerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await users.AnyAsync(cancellationToken))
        {
            return false;
        }

        PasswordPolicy.Check(command.Password);
        users.Add(User.Create(
            command.Email,
            command.DisplayName,
            UserRole.Owner,
            passwordHasher.Hash(command.Password),
            timeProvider.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
