using AutoFlow.Application.Abstractions;
using AutoFlow.Application.Common;
using AutoFlow.Domain.Users;

namespace AutoFlow.Application.Users;

public sealed record GetUserQuery(Guid UserId);

/// <summary>Returns one account, active or not.</summary>
public sealed class GetUserHandler(IUserRepository users)
{
    public async Task<Result<UserDetails>> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await users.GetByIdAsync(query.UserId, cancellationToken);

        return user is null
            ? Result.Failure<UserDetails>(UserErrors.NotFound(query.UserId))
            : Result.Success(UserDetails.From(user));
    }
}

public sealed record ListUsersQuery;

/// <summary>Lists every account, active or not, by display name.</summary>
public sealed class ListUsersHandler(IUserRepository users)
{
    public async Task<IReadOnlyList<UserDetails>> HandleAsync(ListUsersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await users.ListAsync(cancellationToken);

        return [.. all.Select(UserDetails.From)];
    }
}

/// <param name="UserId">The account to change.</param>
/// <param name="Role">Name of the new <see cref="UserRole"/>.</param>
public sealed record ChangeUserRoleCommand(Guid UserId, string Role);

/// <summary>
/// Gives an account another role. Demoting the last active owner is a
/// conflict (409): the workshop would lock itself out of its own settings.
/// </summary>
public sealed class ChangeUserRoleHandler(IUserRepository users, IUnitOfWork unitOfWork)
{
    public async Task<Result<UserDetails>> HandleAsync(ChangeUserRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!EnumNames.TryParse<UserRole>(command.Role, out var role))
        {
            return Result.Failure<UserDetails>(UserErrors.UnknownRole(command.Role));
        }

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDetails>(UserErrors.NotFound(command.UserId));
        }

        if (user.IsActive
            && user.Role == UserRole.Owner
            && role != UserRole.Owner
            && await users.CountActiveOwnersAsync(cancellationToken) <= 1)
        {
            return Result.Failure<UserDetails>(UserErrors.LastOwner());
        }

        user.ChangeRole(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserDetails.From(user));
    }
}

/// <param name="UserId">The account to deactivate.</param>
/// <param name="ActingUserId">Who is asking; nobody can deactivate themselves.</param>
public sealed record DeactivateUserCommand(Guid UserId, Guid ActingUserId);

/// <summary>
/// Stops an account from signing in; tokens it already holds stop working
/// on their next request. Deactivating yourself or the last active owner is
/// a conflict (409); an account that is already inactive breaks a rule (422).
/// </summary>
public sealed class DeactivateUserHandler(IUserRepository users, IUnitOfWork unitOfWork)
{
    public async Task<Result<UserDetails>> HandleAsync(DeactivateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDetails>(UserErrors.NotFound(command.UserId));
        }

        if (user.Id == command.ActingUserId)
        {
            return Result.Failure<UserDetails>(UserErrors.CannotDeactivateSelf());
        }

        if (user.IsActive
            && user.Role == UserRole.Owner
            && await users.CountActiveOwnersAsync(cancellationToken) <= 1)
        {
            return Result.Failure<UserDetails>(UserErrors.LastOwner());
        }

        user.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserDetails.From(user));
    }
}

public sealed record ReactivateUserCommand(Guid UserId);

/// <summary>Lets a deactivated account sign in again. An active account breaks a rule (422).</summary>
public sealed class ReactivateUserHandler(IUserRepository users, IUnitOfWork unitOfWork)
{
    public async Task<Result<UserDetails>> HandleAsync(ReactivateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDetails>(UserErrors.NotFound(command.UserId));
        }

        user.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserDetails.From(user));
    }
}

/// <param name="UserId">The signed-in user.</param>
/// <param name="CurrentPassword">Proves it is really them, not someone at an unlocked screen.</param>
/// <param name="NewPassword">Must meet the <see cref="PasswordPolicy"/>.</param>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword);

/// <summary>
/// Lets signed-in users change their own password. A wrong current password
/// is a validation error (400); a new password that breaks the policy breaks
/// a rule (422).
/// </summary>
public sealed class ChangePasswordHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<UserDetails>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDetails>(UserErrors.NotFound(command.UserId));
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.CurrentPassword ?? string.Empty))
        {
            return Result.Failure<UserDetails>(UserErrors.WrongCurrentPassword());
        }

        PasswordPolicy.Check(command.NewPassword);
        user.ChangePasswordHash(passwordHasher.Hash(command.NewPassword));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserDetails.From(user));
    }
}
