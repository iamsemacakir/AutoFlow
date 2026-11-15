using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Users;

/// <param name="Email">The new user's login.</param>
/// <param name="DisplayName">The name shown in the application.</param>
/// <param name="Role">Name of a <see cref="UserRole"/>: <c>Owner</c>, <c>ServiceAdvisor</c> or <c>Mechanic</c>.</param>
/// <param name="Password">The initial password; the user can change it after signing in.</param>
public sealed record CreateUserCommand(string Email, string DisplayName, string Role, string Password);

/// <summary>
/// Opens a staff account. An unknown role is a validation error (400), an
/// e-mail address in use is a conflict (409), a password that breaks the
/// policy or a malformed address breaks a rule (422).
/// </summary>
public sealed class CreateUserHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<UserDetails>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!EnumNames.TryParse<UserRole>(command.Role, out var role))
        {
            return Result.Failure<UserDetails>(UserErrors.UnknownRole(command.Role));
        }

        PasswordPolicy.Check(command.Password);

        // The domain validates the address and the name before anything is looked up.
        var user = User.Create(
            command.Email,
            command.DisplayName,
            role,
            passwordHasher.Hash(command.Password),
            timeProvider.GetUtcNow());

        if (await users.ExistsWithEmailAsync(user.Email, cancellationToken))
        {
            return Result.Failure<UserDetails>(UserErrors.DuplicateEmail(user.Email.Value));
        }

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserDetails.From(user));
    }
}
